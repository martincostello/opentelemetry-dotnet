// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Attributes;
using OpenTelemetry.Context.Propagation;

namespace Benchmarks.Context.Propagation;

[MemoryDiagnoser]
public class TraceparentParsingBenchmarks
{
    private static readonly TraceContextPropagator Propagator = new();
    private string traceparent = string.Empty;
    private string[] headers = [];

    [Params("Valid", "InvalidHex", "UppercaseHex", "ZeroId")]
    public string Scenario { get; set; } = "Valid";

    [GlobalSetup]
    public void Setup()
    {
        var traceId = this.Scenario switch
        {
            "InvalidHex" => "0af7651916cd43dd8448eb211c80319g",
            "UppercaseHex" => "0af7651916cd43dd8448eb211c80319C",
            "ZeroId" => "00000000000000000000000000000000",
            _ => "0af7651916cd43dd8448eb211c80319c",
        };

        this.traceparent = $"00-{traceId}-b9c7c989f97918e1-01";
        this.headers = [this.traceparent];
    }

    [Benchmark]
    public bool Parse()
        => TraceContextPropagator.TryExtractTraceparent(this.traceparent, out _, out _, out _);

    [Benchmark]
    public PropagationContext Extract()
        => Propagator.Extract(default, this.headers, static (carrier, name) => name == "traceparent" ? carrier : []);
}
