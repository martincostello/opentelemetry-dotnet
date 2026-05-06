// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenTelemetry.Metrics;

namespace OpenTelemetry.Exporter.Prometheus;

internal sealed class PrometheusCollectionManager
{
    private const int MaxCachedMetrics = 1024;

    private readonly Dictionary<PrometheusProtocol, byte[]> buffers = [];
    private readonly Dictionary<PrometheusProtocol, ArraySegment<byte>> previousViews = [];
    private readonly Dictionary<PrometheusProtocol, DateTime> previouslyGeneratedAtUtc = [];

    private readonly PrometheusExporter exporter;
    private readonly int scrapeResponseCacheDurationMilliseconds;
    private readonly PrometheusExporter.ExportFunc onCollectRef;
    private readonly Dictionary<Metric, PrometheusMetric> metricsCache;
    private readonly HashSet<string> scopes;
    private int metricsCacheCount;
    private int targetInfoBufferLength = -1; // zero or positive when target_info has been written for the first time
    private int globalLockState;
    private int readerCount;
    private bool collectionRunning;
    private TaskCompletionSource<CollectionResponse>? collectionTcs;

    public PrometheusCollectionManager(PrometheusExporter exporter)
    {
        this.exporter = exporter;
        this.scrapeResponseCacheDurationMilliseconds = this.exporter.ScrapeResponseCacheDurationMilliseconds;
        this.onCollectRef = this.OnCollect;
        this.metricsCache = [];
        this.scopes = [];
    }

    internal Func<DateTime> UtcNow { get; set; } = static () => DateTime.UtcNow;

#if NET
    public ValueTask<CollectionResponse> EnterCollect(PrometheusProtocol protocol)
#else
    public Task<CollectionResponse> EnterCollect(PrometheusProtocol protocol)
#endif
    {
        this.EnterGlobalLock();

        try
        {
            // If we are within {ScrapeResponseCacheDurationMilliseconds} of the
            // last successful collect, return the previous view.
            if (this.previouslyGeneratedAtUtc.TryGetValue(protocol, out var timestamp)
                && this.scrapeResponseCacheDurationMilliseconds > 0
                && timestamp.AddMilliseconds(this.scrapeResponseCacheDurationMilliseconds) >= this.UtcNow())
            {
                var view = this.previousViews[protocol];
                var collectionResponse = new CollectionResponse(view, timestamp, fromCache: true);

#if NET
                return new ValueTask<CollectionResponse>(collectionResponse);
#else
                return Task.FromResult(collectionResponse);
#endif
            }

            // If a collection is already running, return a task to wait on the result.
            if (this.collectionRunning)
            {
                this.collectionTcs ??= new TaskCompletionSource<CollectionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

#if NET
                return new ValueTask<CollectionResponse>(this.collectionTcs.Task);
#else
                return this.collectionTcs.Task;
#endif
            }

            this.WaitForReadersToComplete();

            // Start a collection on the current thread.
            this.collectionRunning = true;

            this.previousViews.Remove(protocol);
            this.previouslyGeneratedAtUtc.Remove(protocol);
        }
        finally
        {
            Interlocked.Increment(ref this.readerCount);
            this.ExitGlobalLock();
        }

        CollectionResponse response;
        var result = this.ExecuteCollect(protocol);

        if (result)
        {
            var generatedAt = this.UtcNow();

            this.previouslyGeneratedAtUtc[protocol] = generatedAt;
            var view = this.previousViews[protocol];

            response = new CollectionResponse(view, generatedAt, fromCache: false);
        }
        else
        {
            response = default;
        }

        this.EnterGlobalLock();

        try
        {
            this.collectionRunning = false;
            this.collectionTcs?.SetResult(response);
            this.collectionTcs = null;
        }
        finally
        {
            this.ExitGlobalLock();
        }

#if NET
        return new ValueTask<CollectionResponse>(response);
#else
        return Task.FromResult(response);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExitCollect()
        => Interlocked.Decrement(ref this.readerCount);

    private static bool IncreaseBufferSize(ref byte[] buffer)
    {
        var newBufferSize = buffer.Length * 2;

        if (newBufferSize > 100 * 1024 * 1024)
        {
            return false;
        }

        var newBuffer = new byte[newBufferSize];
        buffer.CopyTo(newBuffer, 0);
        buffer = newBuffer;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnterGlobalLock()
    {
        SpinWait lockWait = default;
        while (true)
        {
            if (Interlocked.CompareExchange(ref this.globalLockState, 1, this.globalLockState) != 0)
            {
                lockWait.SpinOnce();
                continue;
            }

            break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitGlobalLock()
        => Interlocked.Exchange(ref this.globalLockState, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WaitForReadersToComplete()
    {
        SpinWait readWait = default;
        while (true)
        {
            if (Interlocked.CompareExchange(ref this.readerCount, 0, 0) != 0)
            {
                readWait.SpinOnce();
                continue;
            }

            break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ExecuteCollect(PrometheusProtocol protocol)
    {
        this.exporter.OnExport = this.onCollectRef;
        try
        {
            this.exporter.Protocol = protocol;
            return this.exporter.Collect!(Timeout.Infinite);
        }
        finally
        {
            this.exporter.OnExport = null;
        }
    }

    private ExportResult OnCollect(in Batch<Metric> metrics)
    {
        var cursor = 0;
        var protocol = this.exporter.Protocol;

        var serializer = TextFormatSerializer.GetSerializer(protocol);

        if (!this.buffers.ContainsKey(protocol))
        {
            this.buffers[protocol] = new byte[85_000]; // Encourage the object to live in LOH (large object heap)
        }

        ref var buffer = ref CollectionsMarshal.GetValueRefOrNullRef(this.buffers, protocol);

        try
        {
            if (protocol.IsOpenMetrics)
            {
                cursor = this.WriteTargetInfo(ref buffer, serializer);

                this.scopes.Clear();

                foreach (var metric in metrics)
                {
                    if (!serializer.CanWriteMetric(metric))
                    {
                        continue;
                    }

                    if (this.scopes.Add(metric.MeterName))
                    {
                        while (true)
                        {
                            try
                            {
                                cursor = serializer.WriteScopeInfo(buffer, cursor, metric.MeterName);

                                break;
                            }
                            catch (IndexOutOfRangeException)
                            {
                                if (!IncreaseBufferSize(ref buffer))
                                {
                                    // there are two cases we might run into the following condition:
                                    // 1. we have many metrics to be exported - in this case we probably want
                                    //    to put some upper limit and allow the user to configure it.
                                    // 2. we got an IndexOutOfRangeException which was triggered by some other
                                    //    code instead of the buffer[cursor++] - in this case we should give up
                                    //    at certain point rather than allocating like crazy.
                                    throw;
                                }
                            }
                        }
                    }
                }
            }

            var metricStates = this.GetMetricStates(metrics, this.exporter.Protocol, serializer);

            foreach (var metricState in metricStates)
            {
                while (true)
                {
                    try
                    {
                        cursor = serializer.WriteMetric(
                            buffer,
                            cursor,
                            metricState.Metric,
                            metricState.PrometheusMetric,
                            metricState.WriteType,
                            metricState.WriteUnit,
                            metricState.WriteHelp,
                            metricState.Unit,
                            metricState.Help);

                        break;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        if (!IncreaseBufferSize(ref buffer))
                        {
                            throw;
                        }
                    }
                }
            }

            while (true)
            {
                try
                {
                    cursor = serializer.WriteEof(buffer, cursor);
                    break;
                }
                catch (IndexOutOfRangeException)
                {
                    if (!IncreaseBufferSize(ref buffer))
                    {
                        throw;
                    }
                }
            }

            this.previousViews[protocol] = new ArraySegment<byte>(buffer, 0, cursor);

            return ExportResult.Success;
        }
        catch (Exception ex)
        {
            this.previousViews[protocol] = new ArraySegment<byte>([], 0, 0);

            PrometheusExporterEventSource.Log.FailedExport(ex);

            return ExportResult.Failure;
        }
    }

    private int WriteTargetInfo(ref byte[] buffer, TextFormatSerializer serializer)
    {
        if (this.targetInfoBufferLength < 0)
        {
            while (true)
            {
                try
                {
                    this.targetInfoBufferLength = serializer.WriteTargetInfo(buffer, 0, this.exporter.Resource);
                    break;
                }
                catch (IndexOutOfRangeException)
                {
                    if (!IncreaseBufferSize(ref buffer))
                    {
                        throw;
                    }
                }
            }
        }

        return this.targetInfoBufferLength;
    }

    private PrometheusMetric GetPrometheusMetric(Metric metric)
    {
        // Optimize writing metrics with bounded cache that has pre-calculated Prometheus names.
        if (!this.metricsCache.TryGetValue(metric, out var prometheusMetric))
        {
            prometheusMetric = PrometheusMetric.Create(metric, this.exporter.DisableTotalNameSuffixForCounters);

            // Add to the cache if there is space.
            if (this.metricsCacheCount < MaxCachedMetrics)
            {
                this.metricsCache[metric] = prometheusMetric;
                this.metricsCacheCount++;
            }
        }

        return prometheusMetric;
    }

    private List<MetricState> GetMetricStates(in Batch<Metric> metrics, PrometheusProtocol protocol, TextFormatSerializer serializer)
    {
        var precomputedMetricStates = new List<PrecomputedMetricState>();
        var metadataStates = new Dictionary<string, MetadataState>(StringComparer.Ordinal);
        var droppedMetricNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metric in metrics)
        {
            if (!serializer.CanWriteMetric(metric))
            {
                continue;
            }

            var prometheusMetric = this.GetPrometheusMetric(metric);
            var metadataName = protocol.IsOpenMetrics ? prometheusMetric.OpenMetricsMetadataName : prometheusMetric.Name;
            precomputedMetricStates.Add(new PrecomputedMetricState(metric, prometheusMetric, metadataName));

            if (!metadataStates.TryGetValue(metadataName, out var metadataState))
            {
                metadataStates[metadataName] = new MetadataState(
                    prometheusMetric.Type,
                    string.IsNullOrEmpty(metric.Description) ? null : metric.Description,
                    string.IsNullOrEmpty(prometheusMetric.Unit) ? null : prometheusMetric.Unit);
                continue;
            }

            if (metadataState.Type != prometheusMetric.Type)
            {
                droppedMetricNames.Add(metadataName);
                PrometheusExporterEventSource.Log.ConflictingType(metadataName, metadataState.Type, prometheusMetric.Type);
            }

            if (!string.IsNullOrEmpty(prometheusMetric.Unit) &&
                metadataState.Unit == null)
            {
                metadataState = new MetadataState(metadataState.Type, metadataState.Help, prometheusMetric.Unit);
                metadataStates[metadataName] = metadataState;
            }
            else if (!string.IsNullOrEmpty(prometheusMetric.Unit) &&
                     metadataState.Unit != null &&
                     metadataState.Unit != prometheusMetric.Unit)
            {
                PrometheusExporterEventSource.Log.ConflictingUnit(metadataName, metadataState.Unit, prometheusMetric.Unit!);
            }

            if (!string.IsNullOrEmpty(metric.Description) &&
                metadataState.Help == null)
            {
                metadataState = new MetadataState(metadataState.Type, metric.Description, metadataState.Unit);
                metadataStates[metadataName] = metadataState;
            }
            else if (!string.IsNullOrEmpty(metric.Description) &&
                     metadataState.Help != null &&
                     metadataState.Help != metric.Description)
            {
                PrometheusExporterEventSource.Log.ConflictingHelp(metadataName, metadataState.Help, metric.Description);
            }
        }

        var metricStates = new List<MetricState>(precomputedMetricStates.Count);
        var emittedMetricNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metricState in precomputedMetricStates)
        {
            if (droppedMetricNames.Contains(metricState.MetadataName))
            {
                continue;
            }

            var writeMetadata = emittedMetricNames.Add(metricState.MetadataName);
            var metadataState = metadataStates[metricState.MetadataName];

            metricStates.Add(
                new MetricState(
                    metricState.Metric,
                    metricState.PrometheusMetric,
                    writeMetadata,
                    writeMetadata && metadataState.Unit != null,
                    writeMetadata && metadataState.Help != null,
                    metadataState.Unit,
                    metadataState.Help));
        }

        return metricStates;
    }

    public readonly struct CollectionResponse
    {
        public CollectionResponse(ArraySegment<byte> view, DateTime generatedAtUtc, bool fromCache)
        {
            this.View = view;
            this.GeneratedAtUtc = generatedAtUtc;
            this.FromCache = fromCache;
        }

        public readonly ArraySegment<byte> View { get; }

        public readonly DateTime GeneratedAtUtc { get; }

        public readonly bool FromCache { get; }
    }

    private readonly struct MetricState
    {
        public MetricState(
            Metric metric,
            PrometheusMetric prometheusMetric,
            bool writeType,
            bool writeUnit,
            bool writeHelp,
            string? unit,
            string? help)
        {
            this.Metric = metric;
            this.PrometheusMetric = prometheusMetric;
            this.WriteType = writeType;
            this.WriteUnit = writeUnit;
            this.WriteHelp = writeHelp;
            this.Unit = unit;
            this.Help = help;
        }

        public readonly Metric Metric { get; }

        public readonly PrometheusMetric PrometheusMetric { get; }

        public readonly bool WriteType { get; }

        public readonly bool WriteUnit { get; }

        public readonly bool WriteHelp { get; }

        public readonly string? Unit { get; }

        public readonly string? Help { get; }
    }

    private readonly struct PrecomputedMetricState
    {
        public PrecomputedMetricState(Metric metric, PrometheusMetric prometheusMetric, string metadataName)
        {
            this.Metric = metric;
            this.PrometheusMetric = prometheusMetric;
            this.MetadataName = metadataName;
        }

        public readonly Metric Metric { get; }

        public readonly PrometheusMetric PrometheusMetric { get; }

        public readonly string MetadataName { get; }
    }

    private readonly struct MetadataState
    {
        public MetadataState(PrometheusType type, string? help, string? unit)
        {
            this.Type = type;
            this.Help = help;
            this.Unit = unit;
        }

        public readonly PrometheusType Type { get; }

        public readonly string? Help { get; }

        public readonly string? Unit { get; }
    }
}
