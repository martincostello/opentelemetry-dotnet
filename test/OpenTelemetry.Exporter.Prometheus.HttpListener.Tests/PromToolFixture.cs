// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NETFRAMEWORK
using System.Net.Http;
#endif
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace OpenTelemetry.Exporter.Prometheus.Tests;

public sealed class PromToolFixture : PrometheusFixture
{
    private const string DockerInternalHost = "host.docker.internal";

    public async Task<ExecResult> CheckMetricsAsync(
        Uri targetUri,
        string accept,
        CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        httpClient.BaseAddress = targetUri;

        if (!string.IsNullOrEmpty(accept))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", accept);
        }

        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Host", targetUri.Host);

        using var response = await httpClient.GetAsync(targetUri, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var format =
            response.Content.Headers.ContentType?.MediaType?.StartsWith(
            "application/openmetrics-text",
            StringComparison.OrdinalIgnoreCase) is true
            ? "openmetrics"
            : "prometheus";

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "C:\\Users\\marti\\.copilot\\session-state\\1eb2291e-ff83-48ab-87db-d8efdd859a62\\files\\promtool.exe",
                Arguments = $"check metrics --lint=all --format={format}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();

        await process.StandardInput.WriteAsync(text).ConfigureAwait(false);
        process.StandardInput.Close();

        var promOutTask = process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        var promErrTask = process.StandardError.ReadToEndAsync().ConfigureAwait(false);

        process.WaitForExit();

        var stdout = await promOutTask;
        var stderr = await promErrTask;

        return new(text + Environment.NewLine + stdout, stderr, process.ExitCode);
    }

    protected override IContainer CreateContainer() =>
        new ContainerBuilder(this.GetImage())
            .WithEntrypoint("sh", "-c")
            .WithCommand("sleep infinity")
            .WithExtraHost(DockerInternalHost, "host-gateway")
            .Build();
}
