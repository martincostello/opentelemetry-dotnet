// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace OpenTelemetry.Exporter.Prometheus;

internal static class PrometheusHeadersParser
{
    private const string OpenMetricsMediaType = "application/openmetrics-text";
    private const string PrometheusTextMediaType = "text/plain";

    internal static bool AcceptsOpenMetrics(string? contentType)
    {
        var value = contentType.AsSpan();
        double? bestOpenMetricsQuality = null;
        double? bestPrometheusQuality = null;

        while (value.Length > 0)
        {
            var headerValue = TrimWhitespace(SplitNext(ref value, ','));
            var mediaType = TrimWhitespace(SplitNext(ref headerValue, ';'));
            var quality = 1.0;

            while (headerValue.Length > 0)
            {
                var parameter = TrimWhitespace(SplitNext(ref headerValue, ';'));
                if (!parameter.StartsWith("q=".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (double.TryParse(parameter.Slice(2), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedQuality))
                {
                    quality = parsedQuality;
                }
            }

            if (mediaType.Equals(OpenMetricsMediaType.AsSpan(), StringComparison.Ordinal))
            {
                bestOpenMetricsQuality = !bestOpenMetricsQuality.HasValue || quality > bestOpenMetricsQuality.Value
                    ? quality
                    : bestOpenMetricsQuality.Value;
            }
            else if (mediaType.Equals(PrometheusTextMediaType.AsSpan(), StringComparison.Ordinal))
            {
                bestPrometheusQuality = !bestPrometheusQuality.HasValue || quality > bestPrometheusQuality.Value
                    ? quality
                    : bestPrometheusQuality.Value;
            }
        }

        return bestOpenMetricsQuality.HasValue
            && (!bestPrometheusQuality.HasValue || bestOpenMetricsQuality.Value >= bestPrometheusQuality.Value);
    }

    private static ReadOnlySpan<char> SplitNext(ref ReadOnlySpan<char> span, char character)
    {
        var index = span.IndexOf(character);

        if (index == -1)
        {
            var part = span;
            span = span.Slice(span.Length);

            return part;
        }
        else
        {
            var part = span.Slice(0, index);
            span = span.Slice(index + 1);

            return part;
        }
    }

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        var end = value.Length - 1;
        while (end >= start && char.IsWhiteSpace(value[end]))
        {
            end--;
        }

        return value.Slice(start, (end - start) + 1);
    }
}
