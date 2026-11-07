// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Attributes;
using OpenTelemetry.Exporter.Prometheus;

namespace Benchmarks.Exporter;

[MemoryDiagnoser]
public class PrometheusHeadersParserBenchmarks
{
    private string accept = string.Empty;

    [Params("Text", "OpenMetrics", "Weighted", "Unsupported")]
    public string Scenario { get; set; } = "Text";

    [GlobalSetup]
    public void Setup()
    {
        this.accept = this.Scenario switch
        {
            "Text" => "text/plain",
            "OpenMetrics" => "application/openmetrics-text; version=1.0.0",
            "Weighted" => "application/openmetrics-text;version=1.0.0;escaping=allow-utf-8;q=0.6,"
                + "application/openmetrics-text;version=0.0.1;q=0.5,"
                + "text/plain;version=1.0.0;escaping=allow-utf-8;q=0.4,"
                + "text/plain;version=0.0.4;q=0.3,*/*;q=0.2",
            "Unsupported" => "application/json",
            _ => throw new InvalidOperationException($"Unknown scenario: {this.Scenario}"),
        };
    }

    [Benchmark]
    public string Negotiate()
        => PrometheusHeadersParser.Negotiate(this.accept).MediaType;
}
