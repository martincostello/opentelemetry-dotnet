// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using OpenTelemetry.Trace;

namespace Benchmarks.Context.Propagation;

[MemoryDiagnoser]
public class TraceStateBenchmarks
{
    private static readonly ActivityTraceId TraceId = ActivityTraceId.CreateFromString("0af7651916cd43dd8448eb211c80319c".AsSpan());
    private static readonly ActivitySpanId SpanId = ActivitySpanId.CreateFromString("b9c7c989f97918e1".AsSpan());

    private KeyValuePair<string, string>[] traceState = [];

    [Params(4, 33)]
    public int MembersCount { get; set; }

    [Params(20, 256)]
    public int ValueLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.traceState = Enumerable.Range(0, this.MembersCount)
            .Select(i => new KeyValuePair<string, string>(
                $"k{i:00}",
                new string((char)('a' + (i % 26)), this.ValueLength)))
            .ToArray();
    }

    [Benchmark]
    public string CreateSpanContext()
    {
        var spanContext = new SpanContext(
            TraceId,
            SpanId,
            ActivityTraceFlags.Recorded,
            isRemote: true,
            traceState: this.traceState);

        return ((ActivityContext)spanContext).TraceState ?? string.Empty;
    }
}
