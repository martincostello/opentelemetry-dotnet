// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;

namespace OpenTelemetry.Android.Tests;

/// <summary>
/// Starts the in-process OTLP collector and then drives the Android test app on a
/// connected emulator. The device run is executed once for the whole class: the
/// app emits traces, metrics and logs over OTLP/HTTP to the collector and the
/// tests then assert on what was received.
/// </summary>
/// <remarks>
/// An Android emulator must already be running. The 'android' workload is required to build the app.
/// </remarks>
public sealed class AndroidAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static readonly TimeSpan DeviceRunTimeout = TimeSpan.FromMinutes(20);

    internal OtlpHttpCollector Collector { get; private set; } = null!;

    internal int DeviceRunExitCode { get; private set; }

    internal string DeviceRunOutput { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        this.Collector = await OtlpHttpCollector.StartAsync();

        (this.DeviceRunExitCode, this.DeviceRunOutput) = RunAppOnDevice();
    }

    public async Task DisposeAsync()
    {
        if (this.Collector is not null)
        {
            await this.Collector.DisposeAsync();
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenTelemetry.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (OpenTelemetry.slnx).");
    }

    private static (int ExitCode, string Output) RunAppOnDevice()
    {
        var repoRoot = RepoRoot();
        var project = Path.Combine(repoRoot, "test", "OpenTelemetry.Android.TestApp", "OpenTelemetry.Android.TestApp.csproj");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };

        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(Configuration);
        startInfo.ArgumentList.Add("--disable-build-servers");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start 'dotnet test' for the Android app.");

        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(DeviceRunTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"'dotnet test' for the Android app timed out after {DeviceRunTimeout}.{Environment.NewLine}{output}");
        }

        // Wait (again, with no timeout) for the async output handlers to flush.
        process.WaitForExit();

        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }
}
