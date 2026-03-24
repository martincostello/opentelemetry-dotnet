// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET

using Benchmarks.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks.EndToEnd;

internal sealed class AppServer : IAsyncDisposable
{
    private WebApplication? app;
    private Uri? baseAddress;
    private CollectorFixture? collector;
    private bool disposed;
    private SqlServerFixture? sqlServer;

    public HttpClient CreateHttpClient()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
#pragma warning restore CA2000 // Dispose objects before losing scope

#pragma warning disable CA5400
        return new(handler, disposeHandler: true) { BaseAddress = this.baseAddress };
#pragma warning restore CA5400
    }

    public async Task StartAsync(bool enableTelemetry)
    {
        if (this.app is not null)
        {
            throw new InvalidOperationException("The server is already running.");
        }

        this.sqlServer = new SqlServerFixture();
        await this.sqlServer.StartAsync().ConfigureAwait(false);

        var builder = WebApplication.CreateBuilder([$"--contentRoot={GetContentRoot()}"]);

        builder.WebHost.UseUrls("https://127.0.0.1:0");

        var config = new List<KeyValuePair<string, string?>>()
        {
            KeyValuePair.Create<string, string?>("ConnectionStrings:SqlServer", this.sqlServer.DatabaseContainer.GetConnectionString()),
            KeyValuePair.Create<string, string?>("OTEL_SDK_DISABLED", (!enableTelemetry).ToString()),
        };

        if (enableTelemetry)
        {
            this.collector = new CollectorFixture();
            await this.collector.StartAsync().ConfigureAwait(false);

            var container = this.collector.CollectorContainer;
            var endpoint = new UriBuilder(Uri.UriSchemeHttp, container.Hostname, container.GetMappedPublicPort(4318)).Uri;

            config.Add(KeyValuePair.Create<string, string?>("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint.ToString()));
            config.Add(KeyValuePair.Create<string, string?>("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf"));

#if DEBUG
            // Export data more frequently for easier debugging with a UI
            config.Add(KeyValuePair.Create<string, string?>("OTEL_BLRP_SCHEDULE_DELAY", "5000"));
            config.Add(KeyValuePair.Create<string, string?>("OTEL_BSP_SCHEDULE_DELAY", "5000"));
            config.Add(KeyValuePair.Create<string, string?>("OTEL_METRIC_EXPORT_INTERVAL", "5000"));
#endif
        }

        builder.Configuration.AddInMemoryCollection(config);

        builder.AddBenchmarks();

        this.app = builder.Build();
        this.app.UseBenchmarks();

        await this.app.StartAsync().ConfigureAwait(false);

        var server = this.app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();

        this.baseAddress = addresses!.Addresses
            .Select((p) => new Uri(p))
            .Last();
    }

    public async Task StopAsync()
    {
        if (this.app is not null)
        {
            await this.app.StopAsync().ConfigureAwait(false);
            await this.app.DisposeAsync().ConfigureAwait(false);
            this.app = null;
        }

        if (this.collector is not null)
        {
            await this.collector.DisposeAsync().ConfigureAwait(false);
            this.collector = null;
        }

        if (this.sqlServer is not null)
        {
            await this.sqlServer.DisposeAsync().ConfigureAwait(false);
            this.sqlServer = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (!this.disposed)
        {
            if (this.app is not null)
            {
                await this.app.DisposeAsync().ConfigureAwait(false);
                this.app = null;
            }

            if (this.collector is not null)
            {
                await this.collector.DisposeAsync().ConfigureAwait(false);
                this.collector = null;
            }

            if (this.sqlServer is not null)
            {
                await this.sqlServer.DisposeAsync().ConfigureAwait(false);
                this.sqlServer = null;
            }
        }

        this.disposed = true;
    }

    private static string? GetRepositoryPath()
    {
        var directoryInfo = new DirectoryInfo(Path.GetDirectoryName(typeof(AppServer).Assembly.Location)!);

        do
        {
            string? solutionPath = Directory.EnumerateFiles(directoryInfo.FullName, "OpenTelemetry.slnx").FirstOrDefault();

            if (solutionPath is not null)
            {
                return Path.GetDirectoryName(solutionPath);
            }

            directoryInfo = directoryInfo.Parent;
        }
        while (directoryInfo is not null);

        return null;
    }

    private static string GetContentRoot() =>
        GetRepositoryPath() is { } repoPath ? Path.GetFullPath(Path.Join(repoPath, "test", "Benchmarks.App")) : string.Empty;
}

#endif
