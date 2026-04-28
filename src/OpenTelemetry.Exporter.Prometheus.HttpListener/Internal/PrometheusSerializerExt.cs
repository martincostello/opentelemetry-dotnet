// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using OpenTelemetry.Metrics;

namespace OpenTelemetry.Exporter.Prometheus;

/// <summary>
/// OpenTelemetry additions to the PrometheusSerializer.
/// </summary>
internal static partial class PrometheusSerializer
{
    public static bool CanWriteMetric(Metric metric)
    {
        if (metric.MetricType == MetricType.ExponentialHistogram)
        {
            // Exponential histograms are not yet support by Prometheus.
            // They are ignored for now.
            return false;
        }

        return true;
    }

    public static int WriteMetric(
        byte[] buffer,
        int cursor,
        Metric metric,
        PrometheusMetric prometheusMetric,
        bool openMetricsRequested,
        bool writeType = true,
        bool writeUnit = true,
        bool writeHelp = true)
    {
        if (writeType)
        {
            cursor = WriteTypeMetadata(buffer, cursor, prometheusMetric, openMetricsRequested);
        }

        if (writeUnit)
        {
            cursor = WriteUnitMetadata(buffer, cursor, prometheusMetric, openMetricsRequested);
        }

        if (writeHelp)
        {
            cursor = WriteHelpMetadata(buffer, cursor, prometheusMetric, metric.Description, openMetricsRequested);
        }

        if (!metric.MetricType.IsHistogram())
        {
            var isLongValue = ((int)metric.MetricType & 0b_0000_1111) == 0x0a /* I8 */;

            foreach (ref readonly var metricPoint in metric.GetMetricPoints())
            {
                // Counter and Gauge
                cursor = WriteMetricName(buffer, cursor, prometheusMetric, openMetricsRequested);
                cursor = WriteTags(buffer, cursor, metric, metricPoint.Tags);

                buffer[cursor++] = unchecked((byte)' ');

                // TODO: MetricType is same for all MetricPoints
                // within a given Metric, so this check can avoided
                // for each MetricPoint
                if (isLongValue)
                {
                    cursor = metric.MetricType.IsSum()
                        ? WriteLong(buffer, cursor, metricPoint.GetSumLong())
                        : WriteLong(buffer, cursor, metricPoint.GetGaugeLastValueLong());
                }
                else
                {
                    cursor = metric.MetricType.IsSum()
                        ? WriteDouble(buffer, cursor, metricPoint.GetSumDouble())
                        : WriteDouble(buffer, cursor, metricPoint.GetGaugeLastValueDouble());
                }

                if (openMetricsRequested
                    && prometheusMetric.Type == PrometheusType.Counter
                    && TryGetLatestExemplar(metricPoint, out var exemplar))
                {
                    cursor = WriteExemplar(buffer, cursor, in exemplar, isLongValue);
                }

                buffer[cursor++] = ASCII_LINEFEED;

                if (prometheusMetric.Type == PrometheusType.Counter)
                {
                    cursor = WriteCreatedMetric(buffer, cursor, metric, prometheusMetric, metricPoint, openMetricsRequested);
                }
            }
        }
        else
        {
            foreach (ref readonly var metricPoint in metric.GetMetricPoints())
            {
                var tags = metricPoint.Tags;
                var previousBound = double.NegativeInfinity;

                long totalCount = 0;
                var hasNegativeBucketBounds = false;
                foreach (var histogramMeasurement in metricPoint.GetHistogramBuckets())
                {
                    if (openMetricsRequested && histogramMeasurement.ExplicitBound < 0)
                    {
                        hasNegativeBucketBounds = true;
                    }

                    totalCount += histogramMeasurement.BucketCount;

                    cursor = WriteMetricName(buffer, cursor, prometheusMetric, openMetricsRequested);
                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "_bucket{");
                    cursor = WriteTags(buffer, cursor, metric, tags, writeEnclosingBraces: false);
                    buffer[cursor++] = unchecked((byte)',');

                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "le=\"");

                    if (histogramMeasurement.ExplicitBound != double.PositiveInfinity)
                    {
                        cursor = openMetricsRequested
                            ? WriteCanonicalLabelValue(buffer, cursor, histogramMeasurement.ExplicitBound)
                            : WriteDouble(buffer, cursor, histogramMeasurement.ExplicitBound);
                    }
                    else
                    {
                        cursor = WriteAsciiStringNoEscape(buffer, cursor, "+Inf");
                    }

                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "\"} ");

                    cursor = WriteLong(buffer, cursor, totalCount);

                    if (openMetricsRequested
                        && TryGetLatestHistogramBucketExemplar(metricPoint, previousBound, histogramMeasurement.ExplicitBound, out var exemplar))
                    {
                        cursor = WriteExemplar(buffer, cursor, in exemplar, isLongValue: false);
                    }

                    buffer[cursor++] = ASCII_LINEFEED;
                    previousBound = histogramMeasurement.ExplicitBound;
                }

                if (!openMetricsRequested || !hasNegativeBucketBounds)
                {
                    // OpenMetrics histograms with negative bucket thresholds MUST NOT expose
                    // _sum and therefore MUST NOT expose _count.
                    // See https://prometheus.io/docs/specs/om/open_metrics_spec/#histogram-1
                    cursor = WriteMetricName(buffer, cursor, prometheusMetric, openMetricsRequested);
                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "_sum");
                    cursor = WriteTags(buffer, cursor, metric, metricPoint.Tags);

                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteDouble(buffer, cursor, metricPoint.GetHistogramSum());

                    buffer[cursor++] = ASCII_LINEFEED;

                    // Histogram count
                    cursor = WriteMetricName(buffer, cursor, prometheusMetric, openMetricsRequested);
                    cursor = WriteAsciiStringNoEscape(buffer, cursor, "_count");
                    cursor = WriteTags(buffer, cursor, metric, metricPoint.Tags);

                    buffer[cursor++] = unchecked((byte)' ');

                    cursor = WriteLong(buffer, cursor, metricPoint.GetHistogramCount());

                    buffer[cursor++] = ASCII_LINEFEED;
                }

                cursor = WriteCreatedMetric(buffer, cursor, metric, prometheusMetric, metricPoint, openMetricsRequested);
            }
        }

        return cursor;
    }

    private static bool TryGetLatestExemplar(in MetricPoint metricPoint, out Exemplar exemplar)
    {
        exemplar = default;
        if (!metricPoint.TryGetExemplars(out var exemplars))
        {
            return false;
        }

        var found = false;
        foreach (var candidate in exemplars)
        {
            if (!found || exemplar.Timestamp < candidate.Timestamp)
            {
                exemplar = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool TryGetLatestHistogramBucketExemplar(in MetricPoint metricPoint, double lowerBoundExclusive, double upperBoundInclusive, out Exemplar exemplar)
    {
        exemplar = default;
        if (!metricPoint.TryGetExemplars(out var exemplars))
        {
            return false;
        }

        var found = false;
        foreach (var candidate in exemplars)
        {
            var exemplarValue = candidate.DoubleValue;
            if (double.IsNaN(exemplarValue))
            {
                continue;
            }

            if (exemplarValue <= upperBoundInclusive && exemplarValue > lowerBoundExclusive)
            {
                if (!found || exemplar.Timestamp < candidate.Timestamp)
                {
                    exemplar = candidate;
                    found = true;
                }
            }
        }

        return found;
    }
}
