// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Frozen;
using BenchmarkDotNet.Attributes;
using OpenTelemetry.Internal;

namespace Benchmarks.Diagnostics;

[MemoryDiagnoser]
public class ResourceAttributeRedactionBenchmarks
{
    private static readonly FrozenSet<string> SafeKeys = new[]
    {
        "deployment.environment",
        "deployment.environment.name",
        "service.instance.id",
        "service.name",
        "service.namespace",
        "service.version",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SafePrefixes =
    [
        "cloud.",
        "container.",
        "host.",
        "k8s.",
        "os.",
        "process.runtime.",
        "telemetry.distro.",
        "telemetry.sdk.",
    ];

    private string value = string.Empty;

    [Params(3, 12, 40)]
    public int AttributeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var attributes = new string[this.AttributeCount];

        for (var i = 0; i < attributes.Length; i++)
        {
            attributes[i] = i switch
            {
                0 => "service.name=checkout",
                1 => "deployment.environment=production",
                2 => "k8s.namespace.name=payments",
                _ => $"tenant.attribute{i}=sensitive-value-{i}",
            };
        }

        this.value = string.Join(',', attributes);
    }

    [Benchmark(Baseline = true)]
    public string SplitBased()
    {
        var pairs = this.value.Split(',');
        var builder = StringBuilderCache.Acquire(this.value.Length);

        for (var i = 0; i < pairs.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var pair = pairs[i];
            if (pair.Length == 0)
            {
                continue;
            }

            var parts = pair.Split(['='], 2);
            if (parts.Length != 2)
            {
                builder.Append(SensitiveDataHelper.RedactedValue);
                continue;
            }

            builder.Append(parts[0]).Append('=');
            builder.Append(IsSafeResourceAttributeKey(parts[0].Trim()) ? parts[1] : SensitiveDataHelper.RedactedValue);
        }

        return StringBuilderCache.GetStringAndRelease(builder);
    }

    [Benchmark]
    public string SpanBased()
    {
        var builder = StringBuilderCache.Acquire(this.value.Length);
        var remaining = this.value.AsSpan();
        var first = true;

        while (true)
        {
            var separator = remaining.IndexOf(',');
            var pair = separator < 0 ? remaining : remaining[..separator];

            if (!first)
            {
                builder.Append(',');
            }

            if (!pair.IsEmpty)
            {
                var equals = pair.IndexOf('=');
                if (equals < 0)
                {
                    builder.Append(SensitiveDataHelper.RedactedValue);
                }
                else
                {
                    var key = pair[..equals];
                    builder.Append(key).Append('=');
                    builder.Append(IsSafeResourceAttributeKey(key.Trim()) ? pair[(equals + 1)..] : SensitiveDataHelper.RedactedValue);
                }
            }

            if (separator < 0)
            {
                break;
            }

            remaining = remaining[(separator + 1)..];
            first = false;
        }

        return StringBuilderCache.GetStringAndRelease(builder);
    }

    private static bool IsSafeResourceAttributeKey(string key)
    {
        if (SafeKeys.Contains(key))
        {
            return true;
        }

        foreach (var prefix in SafePrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSafeResourceAttributeKey(ReadOnlySpan<char> key)
    {
        var alternateLookup = SafeKeys.GetAlternateLookup<ReadOnlySpan<char>>();
        if (alternateLookup.Contains(key))
        {
            return true;
        }

        foreach (var prefix in SafePrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
