// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Benchmarks.EndToEnd;

public sealed class CollectorFixture : IAsyncDisposable
{
    private static readonly string CollectorImage = GetCollectorImage();

    public IContainer CollectorContainer { get; } = CreateCollector();

    public Task StartAsync() => this.CollectorContainer.StartAsync();

    public async ValueTask DisposeAsync() => await this.CollectorContainer.DisposeAsync().ConfigureAwait(false);

    private static IContainer CreateCollector() =>
        new ContainerBuilder(CollectorImage)
            .WithPortBinding(3000)
            .WithPortBinding(4317)
            .WithPortBinding(4318)
            .WithPortBinding(9090)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(3000)))
            .Build();

    private static string GetCollectorImage()
    {
        var assembly = typeof(CollectorFixture).Assembly;

#pragma warning disable IDE0370 // Suppression is unnecessary
        using var stream = assembly.GetManifestResourceStream("lgtm.Dockerfile");
        using var reader = new StreamReader(stream!);
#pragma warning restore IDE0370 // Suppression is unnecessary

        var raw = reader.ReadToEnd();

        // Exclude FROM
        return raw.Substring(4).Trim();
    }
}

#endif
