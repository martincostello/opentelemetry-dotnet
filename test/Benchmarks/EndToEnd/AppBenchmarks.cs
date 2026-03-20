// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace Benchmarks.EndToEnd;

[EventPipeProfiler(EventPipeProfile.CpuSampling)]
[MemoryDiagnoser]
public class AppBenchmarks : IAsyncDisposable
{
    private AppServer? app = new();
    private HttpClient? client;
    private bool disposed;

    [Params(false, true)]
    public bool EnableTelemetry { get; set; }

    [ParamsSource(nameof(BenchmarkEndpoints))]
    public Uri Endpoint { get; set; } = null!;

    public static IEnumerable<Uri> BenchmarkEndpoints() =>
    [
        new("httpclient", UriKind.Relative),
        new("ping", UriKind.Relative),
        new("sqlserver/query", UriKind.Relative),
        new("sqlserver/sproc", UriKind.Relative),
    ];

    [GlobalSetup]
    public async Task StartServer()
    {
        if (this.app is not null)
        {
            await this.app.StartAsync(this.EnableTelemetry).ConfigureAwait(false);
            this.client = this.app.CreateHttpClient();
        }
    }

    [GlobalCleanup]
    public async Task StopServer()
    {
        if (this.app is not null)
        {
            await this.app.StopAsync().ConfigureAwait(false);
            this.app = null;
        }
    }

    [Benchmark]
    public async Task<byte[]> HttpServerRequest()
        => await this.client!.GetByteArrayAsync(this.Endpoint).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (!this.disposed)
        {
            this.client?.Dispose();
            this.client = null;

            if (this.app is not null)
            {
                await this.app.DisposeAsync().ConfigureAwait(false);
                this.app = null;
            }
        }

        this.disposed = true;
    }
}

#endif
