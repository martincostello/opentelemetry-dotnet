// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using OpenTelemetry.Internal;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace OpenTelemetry.Exporter.Prometheus;

/// <summary>
/// Basic PrometheusSerializer which has no OpenTelemetry dependency.
/// </summary>
internal static partial class PrometheusSerializer
{
#pragma warning disable SA1310 // Field name should not contain an underscore
    private const byte ASCII_QUOTATION_MARK = 0x22; // '"'
    private const byte ASCII_REVERSE_SOLIDUS = 0x5C; // '\\'
    private const byte ASCII_LINEFEED = 0x0A; // `\n`
#pragma warning restore SA1310 // Field name should not contain an underscore

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteDouble(byte[] buffer, int cursor, double value)
    {
        if (MathHelper.IsFinite(value))
        {
            // From https://prometheus.io/docs/specs/om/open_metrics_spec/#considerations-canonical-numbers:
            // A warning to implementers in C and other languages that share its printf implementation:
            // The standard precision of %f, %e and %g is only six significant digits. 17 significant
            // digits are required for full precision, e.g. printf("%.17g", d).
#if NET
            Span<char> span = stackalloc char[128];

            var result = value.TryFormat(span, out var cchWritten, "G17", CultureInfo.InvariantCulture);
            Debug.Assert(result, $"{nameof(result)} should be true.");

            for (var i = 0; i < cchWritten; i++)
            {
                buffer[cursor++] = unchecked((byte)span[i]);
            }
#else
            cursor = WriteAsciiStringNoEscape(buffer, cursor, value.ToString("G17", CultureInfo.InvariantCulture));
#endif
        }
        else if (double.IsPositiveInfinity(value))
        {
            cursor = WriteAsciiStringNoEscape(buffer, cursor, "+Inf");
        }
        else if (double.IsNegativeInfinity(value))
        {
            cursor = WriteAsciiStringNoEscape(buffer, cursor, "-Inf");
        }
        else
        {
            // See https://prometheus.io/docs/instrumenting/exposition_formats/#comments-help-text-and-type-information
            Debug.Assert(double.IsNaN(value), $"{nameof(value)} should be NaN.");
            cursor = WriteAsciiStringNoEscape(buffer, cursor, "NaN");
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteCanonicalLabelValue(byte[] buffer, int cursor, double value)
    {
        // Histogram "le" and summary "quantile" label values use OpenMetrics canonical numbers.
        // See https://prometheus.io/docs/specs/om/open_metrics_spec/#considerations-canonical-numbers
        if (double.IsPositiveInfinity(value))
        {
            return WriteAsciiStringNoEscape(buffer, cursor, "+Inf");
        }
        else if (double.IsNegativeInfinity(value))
        {
            return WriteAsciiStringNoEscape(buffer, cursor, "-Inf");
        }
        else if (double.IsNaN(value))
        {
            return WriteAsciiStringNoEscape(buffer, cursor, "NaN");
        }

        var formattedValue = value.ToString("G17", CultureInfo.InvariantCulture);

        var exponentIndex = formattedValue.IndexOf('E', StringComparison.Ordinal);
        if (exponentIndex >= 0)
        {
            formattedValue = string.Concat(formattedValue.AsSpan(0, exponentIndex), "e", formattedValue.AsSpan(exponentIndex + 1));
        }
        else if (formattedValue.IndexOf('.', StringComparison.Ordinal) < 0)
        {
            formattedValue += ".0";
        }

        return WriteAsciiStringNoEscape(buffer, cursor, formattedValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteLong(byte[] buffer, int cursor, long value)
    {
#if NET
        Span<char> span = stackalloc char[20];

        var result = value.TryFormat(span, out var cchWritten, "G", CultureInfo.InvariantCulture);
        Debug.Assert(result, $"{nameof(result)} should be true.");

        for (var i = 0; i < cchWritten; i++)
        {
            buffer[cursor++] = unchecked((byte)span[i]);
        }
#else
        cursor = WriteAsciiStringNoEscape(buffer, cursor, value.ToString(CultureInfo.InvariantCulture));
#endif

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteAsciiStringNoEscape(byte[] buffer, int cursor, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            buffer[cursor++] = unchecked((byte)value[i]);
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteUnicodeNoEscape(byte[] buffer, int cursor, int ordinal)
    {
        // Strings MUST only consist of valid UTF-8 characters.
        // See https://prometheus.io/docs/specs/om/open_metrics_spec/#strings.
        if (ordinal <= 0x7F)
        {
            buffer[cursor++] = unchecked((byte)ordinal);
        }
        else if (ordinal <= 0x07FF)
        {
            buffer[cursor++] = unchecked((byte)(0b_1100_0000 | (ordinal >> 6)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | (ordinal & 0b_0011_1111)));
        }
        else if (ordinal <= 0xFFFF)
        {
            buffer[cursor++] = unchecked((byte)(0b_1110_0000 | (ordinal >> 12)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | ((ordinal >> 6) & 0b_0011_1111)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | (ordinal & 0b_0011_1111)));
        }
        else
        {
            buffer[cursor++] = unchecked((byte)(0b_1111_0000 | (ordinal >> 18)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | ((ordinal >> 12) & 0b_0011_1111)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | ((ordinal >> 6) & 0b_0011_1111)));
            buffer[cursor++] = unchecked((byte)(0b_1000_0000 | (ordinal & 0b_0011_1111)));
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteUnicodeString(byte[] buffer, int cursor, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ordinal = (ushort)value[i];
            switch (ordinal)
            {
                case ASCII_REVERSE_SOLIDUS:
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    break;
                case ASCII_LINEFEED:
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    buffer[cursor++] = unchecked((byte)'n');
                    break;
                default:
                    cursor = WriteUnicodeScalar(buffer, cursor, value, ref i);
                    break;
            }
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteLabelKey(byte[] buffer, int cursor, string value)
    {
        var sanitizedValue = SanitizeLabelKey(value);
        return WriteAsciiStringNoEscape(buffer, cursor, sanitizedValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteLabelValue(byte[] buffer, int cursor, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var ordinal = (ushort)value[i];
            switch (ordinal)
            {
                case ASCII_QUOTATION_MARK:
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    buffer[cursor++] = ASCII_QUOTATION_MARK;
                    break;
                case ASCII_REVERSE_SOLIDUS:
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    break;
                case ASCII_LINEFEED:
                    buffer[cursor++] = ASCII_REVERSE_SOLIDUS;
                    buffer[cursor++] = unchecked((byte)'n');
                    break;
                default:
                    cursor = WriteUnicodeScalar(buffer, cursor, value, ref i);
                    break;
            }
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteLabel(byte[] buffer, int cursor, string labelKey, object? labelValue)
    {
        cursor = WriteLabelKey(buffer, cursor, labelKey);
        buffer[cursor++] = unchecked((byte)'=');
        buffer[cursor++] = unchecked((byte)'"');

        // In Prometheus, a label with an empty label value is considered equivalent to a label that does not exist.
        cursor = WriteLabelValue(buffer, cursor, GetLabelValueString(labelValue));
        buffer[cursor++] = unchecked((byte)'"');

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteMetricName(byte[] buffer, int cursor, PrometheusMetric metric, bool openMetricsRequested)
    {
        // Metric name has already been escaped.
        var name = openMetricsRequested ? metric.OpenMetricsName : metric.Name;

        Debug.Assert(!string.IsNullOrWhiteSpace(name), "name was null or whitespace");

        for (var i = 0; i < name.Length; i++)
        {
            var ordinal = (ushort)name[i];
            buffer[cursor++] = unchecked((byte)ordinal);
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteMetricMetadataName(byte[] buffer, int cursor, PrometheusMetric metric, bool openMetricsRequested)
    {
        // Metric name has already been escaped.
        var name = openMetricsRequested ? metric.OpenMetricsMetadataName : metric.Name;

        Debug.Assert(!string.IsNullOrWhiteSpace(name), "name was null or whitespace");

        for (var i = 0; i < name.Length; i++)
        {
            var ordinal = (ushort)name[i];
            buffer[cursor++] = unchecked((byte)ordinal);
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteEof(byte[] buffer, int cursor)
    {
        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# EOF");
        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteHelpMetadata(byte[] buffer, int cursor, PrometheusMetric metric, string metricDescription, bool openMetricsRequested)
    {
        if (string.IsNullOrEmpty(metricDescription))
        {
            return cursor;
        }

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# HELP ");
        cursor = WriteMetricMetadataName(buffer, cursor, metric, openMetricsRequested);

        if (!string.IsNullOrEmpty(metricDescription))
        {
            buffer[cursor++] = unchecked((byte)' ');
            cursor = WriteUnicodeString(buffer, cursor, metricDescription);
        }

        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteTypeMetadata(byte[] buffer, int cursor, PrometheusMetric metric, bool openMetricsRequested)
    {
        var metricType = MapPrometheusType(metric.Type, openMetricsRequested);

        Debug.Assert(!string.IsNullOrEmpty(metricType), $"{nameof(metricType)} should not be null or empty.");

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# TYPE ");
        cursor = WriteMetricMetadataName(buffer, cursor, metric, openMetricsRequested);
        buffer[cursor++] = unchecked((byte)' ');
        cursor = WriteAsciiStringNoEscape(buffer, cursor, metricType);

        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteUnitMetadata(byte[] buffer, int cursor, PrometheusMetric metric, bool openMetricsRequested)
    {
        if (string.IsNullOrEmpty(metric.Unit))
        {
            return cursor;
        }

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# UNIT ");
        cursor = WriteMetricMetadataName(buffer, cursor, metric, openMetricsRequested);

        buffer[cursor++] = unchecked((byte)' ');

        // Unit name has already been escaped.
#pragma warning disable IDE0370 // Remove unnecessary suppression
        for (var i = 0; i < metric.Unit!.Length; i++)
#pragma warning restore IDE0370 // Remove unnecessary suppression
        {
            var ordinal = (ushort)metric.Unit[i];
            buffer[cursor++] = unchecked((byte)ordinal);
        }

        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteScopeInfo(byte[] buffer, int cursor, Metric metric)
    {
        cursor = WriteScopeInfoMetadata(buffer, cursor);
        return WriteScopeInfoMetric(buffer, cursor, metric);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteScopeInfoMetadata(byte[] buffer, int cursor)
    {
        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# TYPE otel_scope info");
        buffer[cursor++] = ASCII_LINEFEED;

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# HELP otel_scope Scope metadata");
        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteScopeInfoMetric(byte[] buffer, int cursor, Metric metric)
    {
        if (string.IsNullOrEmpty(metric.MeterName))
        {
            return cursor;
        }

        // OpenMetrics info families use the family name in TYPE/HELP metadata and add the
        // "_info" suffix only to the sample metric name.
        // See https://prometheus.io/docs/specs/om/open_metrics_spec/#info-1
        cursor = WriteAsciiStringNoEscape(buffer, cursor, "otel_scope_info");
        cursor = WriteScopeLabels(buffer, cursor, metric);
        buffer[cursor++] = unchecked((byte)' ');
        buffer[cursor++] = unchecked((byte)'1');
        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteTags(byte[] buffer, int cursor, Metric metric, ReadOnlyTagCollection tags, bool writeEnclosingBraces = true)
    {
        List<LabelData>? labels = null;
        AddScopeLabels(metric, ref labels);
        AddMetricPointLabels(tags, ref labels);
        return WriteLabels(buffer, cursor, labels, writeEnclosingBraces);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int WriteTargetInfo(byte[] buffer, int cursor, Resource resource, bool openMetricsRequested)
    {
        if (resource == Resource.Empty)
        {
            return cursor;
        }

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# TYPE ");
        cursor = WriteAsciiStringNoEscape(buffer, cursor, openMetricsRequested ? "target" : "target_info");
        buffer[cursor++] = unchecked((byte)' ');
        cursor = WriteAsciiStringNoEscape(buffer, cursor, openMetricsRequested ? "info" : "gauge");
        buffer[cursor++] = ASCII_LINEFEED;

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "# HELP ");
        cursor = WriteAsciiStringNoEscape(buffer, cursor, openMetricsRequested ? "target" : "target_info");
        cursor = WriteAsciiStringNoEscape(buffer, cursor, " Target metadata");
        buffer[cursor++] = ASCII_LINEFEED;

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "target_info");
        cursor = WriteLabels(buffer, cursor, CreateResourceLabels(resource.Attributes.Select(static a => new KeyValuePair<string, object?>(a.Key, a.Value))), writeEnclosingBraces: true);
        buffer[cursor++] = unchecked((byte)' ');
        buffer[cursor++] = unchecked((byte)'1');
        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    public static int WriteCreatedMetric(
        byte[] buffer,
        int cursor,
        Metric metric,
        PrometheusMetric prometheusMetric,
        in MetricPoint metricPoint,
        bool openMetricsRequested)
    {
        if (metricPoint.StartTime == default)
        {
            return cursor;
        }

        if (openMetricsRequested)
        {
            cursor = WriteMetricMetadataName(buffer, cursor, prometheusMetric, openMetricsRequested: true);
        }
        else
        {
            cursor = WriteMetricName(buffer, cursor, prometheusMetric, openMetricsRequested: false);
        }

        cursor = WriteAsciiStringNoEscape(buffer, cursor, "_created");
        cursor = WriteTags(buffer, cursor, metric, metricPoint.Tags);
        buffer[cursor++] = unchecked((byte)' ');
        cursor = WriteUnixTimeSeconds(buffer, cursor, metricPoint.StartTime);
        buffer[cursor++] = ASCII_LINEFEED;

        return cursor;
    }

    public static int WriteExemplar(
        byte[] buffer,
        int cursor,
        in Exemplar exemplar,
        bool isLongValue,
        ReadOnlyTagCollection? baseTags = null)
    {
        buffer[cursor++] = unchecked((byte)' ');
        buffer[cursor++] = unchecked((byte)'#');
        buffer[cursor++] = unchecked((byte)' ');

        List<LabelData>? labels = null;
        AddExemplarLabels(in exemplar, ref labels);
        cursor = WriteLabels(buffer, cursor, labels, writeEnclosingBraces: true);
        buffer[cursor++] = unchecked((byte)' ');

        cursor = isLongValue
            ? WriteLong(buffer, cursor, exemplar.LongValue)
            : WriteDouble(buffer, cursor, exemplar.DoubleValue);

        if (exemplar.Timestamp != default)
        {
            buffer[cursor++] = unchecked((byte)' ');
            cursor = WriteUnixTimeSeconds(buffer, cursor, exemplar.Timestamp);
        }

        return cursor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WriteUnicodeScalar(byte[] buffer, int cursor, string value, ref int index)
    {
        // Strings MUST only consist of valid UTF-8 characters.
        // See https://prometheus.io/docs/specs/om/open_metrics_spec/#strings.
        var current = value[index];

        if (!char.IsSurrogate(current))
        {
            return WriteUnicodeNoEscape(buffer, cursor, current);
        }

        if (char.IsHighSurrogate(current) && index < value.Length - 1 && char.IsLowSurrogate(value[index + 1]))
        {
            index++;
            return WriteUnicodeNoEscape(buffer, cursor, char.ConvertToUtf32(current, value[index]));
        }

        return WriteUnicodeNoEscape(buffer, cursor, 0xFFFD);
    }

    private static string GetLabelValueString(object? labelValue)
    {
        // TODO: Attribute values should be written as their JSON representation. Extra logic may need to be added here to correctly convert other .NET types.
        // More detail: https://github.com/open-telemetry/opentelemetry-dotnet/issues/4822#issuecomment-1707328495
        if (labelValue is bool booleanValue)
        {
            return booleanValue ? "true" : "false";
        }
        else if (labelValue is double doubleValue)
        {
            return DoubleToString(doubleValue);
        }
        else if (labelValue is float floatValue)
        {
            return DoubleToString(floatValue);
        }

        return labelValue?.ToString() ?? string.Empty;

        static string DoubleToString(double value)
        {
            // From https://prometheus.io/docs/specs/om/open_metrics_spec/#considerations-canonical-numbers:
            // A warning to implementers in C and other languages that share its printf implementation:
            // The standard precision of %f, %e and %g is only six significant digits. 17 significant
            // digits are required for full precision, e.g. printf("%.17g", d).
            if (MathHelper.IsFinite(value))
            {
                return value.ToString("G17", CultureInfo.InvariantCulture);
            }
            else if (double.IsPositiveInfinity(value))
            {
                return "+Inf";
            }
            else if (double.IsNegativeInfinity(value))
            {
                return "-Inf";
            }
            else
            {
                // See https://prometheus.io/docs/instrumenting/exposition_formats/#comments-help-text-and-type-information
                Debug.Assert(double.IsNaN(value), $"{nameof(value)} should be NaN.");
                return "NaN";
            }
        }
    }

    private static string SanitizeLabelKey(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        StringBuilder? sb = null;
        var lastCharUnderscore = false;

        if (value[0] is >= '0' and <= '9')
        {
            sb = new StringBuilder(value.Length + 1);
            sb.Append('_');
            lastCharUnderscore = true;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb?.Append(c);
                lastCharUnderscore = false;
            }
            else
            {
                if (!lastCharUnderscore)
                {
                    sb ??= new StringBuilder(value, 0, i, value.Length + 1);
                    sb.Append('_');
                    lastCharUnderscore = true;
                }
            }
        }

        return sb?.ToString() ?? value;
    }

    private static int WriteScopeLabels(byte[] buffer, int cursor, Metric metric)
    {
        List<LabelData>? labels = null;
        AddScopeLabels(metric, ref labels);
        return WriteLabels(buffer, cursor, labels, writeEnclosingBraces: true);
    }

    private static List<LabelData> CreateResourceLabels(IEnumerable<KeyValuePair<string, object?>> resourceAttributes)
    {
        List<LabelData>? labels = null;

        foreach (var attribute in resourceAttributes)
        {
            AddLabel(attribute.Key, attribute.Key, attribute.Value, ref labels);
        }

        return labels ?? [];
    }

    private static void AddScopeLabels(Metric metric, ref List<LabelData>? labels)
    {
        AddLabel("otel_scope_name", "otel_scope_name", metric.MeterName, ref labels);

        if (!string.IsNullOrEmpty(metric.MeterVersion))
        {
            AddLabel("otel_scope_version", "otel_scope_version", metric.MeterVersion, ref labels);
        }

        if (!string.IsNullOrEmpty(metric.MeterSchemaUrl))
        {
            // Prometheus exporters MUST add scope schema URL as otel_scope_schema_url.
            // See https://opentelemetry.io/docs/specs/otel/compatibility/prometheus_and_openmetrics/#instrumentation-scope-1
            AddLabel("otel_scope_schema_url", "otel_scope_schema_url", metric.MeterSchemaUrl, ref labels);
        }

        if (metric.MeterTags != null)
        {
            foreach (var tag in metric.MeterTags)
            {
                // Scope attributes named name/version/schema_url MUST be dropped to avoid
                // conflicts with otel_scope_name/version/schema_url.
                // See https://opentelemetry.io/docs/specs/otel/compatibility/prometheus_and_openmetrics/#instrumentation-scope-1
                if (tag.Key == "name" || tag.Key == "version" || tag.Key == "schema_url")
                {
                    continue;
                }

                AddLabel($"otel_scope_{tag.Key}", $"otel_scope_{tag.Key}", tag.Value, ref labels);
            }
        }
    }

    private static void AddMetricPointLabels(ReadOnlyTagCollection tags, ref List<LabelData>? labels)
    {
        foreach (var tag in tags)
        {
            AddLabel(tag.Key, tag.Key, tag.Value, ref labels);
        }
    }

    private static void AddExemplarLabels(in Exemplar exemplar, ref List<LabelData>? labels)
    {
        if (exemplar.TraceId != default)
        {
            AddLabel("trace_id", "trace_id", exemplar.TraceId.ToHexString(), ref labels);
        }

        if (exemplar.SpanId != default)
        {
            AddLabel("span_id", "span_id", exemplar.SpanId.ToHexString(), ref labels);
        }

        foreach (var tag in exemplar.FilteredTags)
        {
            if (tag.Key == "trace_id" || tag.Key == "span_id")
            {
                continue;
            }

            AddLabel(tag.Key, tag.Key, tag.Value, ref labels);
        }
    }

    private static void AddLabel(string originalKey, string outputKey, object? value, ref List<LabelData>? labels)
    {
        labels ??= [];
        labels.Add(new LabelData(originalKey, SanitizeLabelKey(outputKey), GetLabelValueString(value)));
    }

    private static int WriteLabels(byte[] buffer, int cursor, IReadOnlyList<LabelData>? labels, bool writeEnclosingBraces)
    {
        if (writeEnclosingBraces)
        {
            buffer[cursor++] = unchecked((byte)'{');
        }

        if (labels != null && labels.Count > 0)
        {
            List<string>? orderedKeys = null;
            Dictionary<string, List<LabelData>>? labelsBySanitizedKey = null;

            foreach (var label in labels)
            {
                orderedKeys ??= [];
                labelsBySanitizedKey ??= [];

                if (!labelsBySanitizedKey.TryGetValue(label.OutputKey, out var bucket))
                {
                    bucket = [];
                    labelsBySanitizedKey[label.OutputKey] = bucket;
                    orderedKeys.Add(label.OutputKey);
                }

                bucket.Add(label);
            }

            Debug.Assert(orderedKeys != null, $"{nameof(orderedKeys)} should not be null.");
            Debug.Assert(labelsBySanitizedKey != null, $"{nameof(labelsBySanitizedKey)} should not be null.");

            foreach (var key in orderedKeys)
            {
                var bucket = labelsBySanitizedKey[key];
                var labelValue = GetMergedLabelValue(bucket);

                cursor = WriteLabel(buffer, cursor, key, labelValue);
                buffer[cursor++] = unchecked((byte)',');
            }

            cursor--;
        }

        if (writeEnclosingBraces)
        {
            buffer[cursor++] = unchecked((byte)'}');
        }

        return cursor;
    }

    private static string GetMergedLabelValue(List<LabelData> labels)
    {
        if (labels.Count == 1)
        {
            return labels[0].Value;
        }

        labels.Sort(static (left, right) => string.CompareOrdinal(left.OriginalKey, right.OriginalKey));

        var sb = new StringBuilder();
        for (var i = 0; i < labels.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            sb.Append(labels[i].Value);
        }

        return sb.ToString();
    }

    private static int WriteUnixTimeSeconds(byte[] buffer, int cursor, DateTimeOffset value)
    {
        cursor = WriteDouble(buffer, cursor, value.ToUnixTimeMilliseconds() / 1000.0);
        return cursor;
    }

    private static string MapPrometheusType(PrometheusType type, bool openMetricsRequested) => type switch
    {
        PrometheusType.Gauge => "gauge",
        PrometheusType.Counter => "counter",
        PrometheusType.Summary => "summary",
        PrometheusType.Histogram => "histogram",

        // OpenMetrics 1.0 uses "unknown" while Prometheus text format 0.0.4 uses "untyped".
        // See https://prometheus.io/docs/specs/om/open_metrics_spec/#unknown-1
        PrometheusType.Untyped or _ => openMetricsRequested ? "unknown" : "untyped",
    };

    private readonly struct LabelData(string originalKey, string outputKey, string value)
    {
        public string OriginalKey { get; } = originalKey;

        public string OutputKey { get; } = outputKey;

        public string Value { get; } = value;
    }
}
