// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NET

using Testcontainers.MsSql;

namespace Benchmarks.EndToEnd;

public sealed class SqlServerFixture : IAsyncDisposable
{
    private static readonly string SqlServerImage = GetSqlServerImage();

    public MsSqlContainer DatabaseContainer { get; } = CreateMsSql();

    public Task StartAsync() => this.DatabaseContainer.StartAsync();

    public async ValueTask DisposeAsync() => await this.DatabaseContainer.DisposeAsync().ConfigureAwait(false);

    private static MsSqlContainer CreateMsSql()
        => new MsSqlBuilder(SqlServerImage).Build();

    private static string GetSqlServerImage()
    {
        var assembly = typeof(SqlServerFixture).Assembly;

#pragma warning disable IDE0370 // Suppression is unnecessary
        using var stream = assembly.GetManifestResourceStream("sqlserver.Dockerfile");
        using var reader = new StreamReader(stream!);
#pragma warning restore IDE0370 // Suppression is unnecessary

        var raw = reader.ReadToEnd();

        // Exclude FROM
        return raw.Substring(4).Trim();
    }
}

#endif
