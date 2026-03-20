// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Data;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Benchmarks.App;

internal static class BenchmarksBuilder
{
    public static WebApplicationBuilder AddBenchmarks(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        if (string.Equals(builder.Configuration["Benchmarks:EnableTelemetry"], bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            builder.Logging.AddOpenTelemetry();

            var telemetry = builder.Services
                .AddOpenTelemetry()
                .ConfigureResource((resource) => resource.AddService("Benchmarks.App"))
                .UseOtlpExporter();

            telemetry.WithMetrics((metrics) =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddSqlClientInstrumentation();
            });

            telemetry.WithTracing((tracing) =>
            {
                tracing.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddSqlClientInstrumentation();
            });
        }

        builder.Services.AddHttpClient();

        builder.Services.AddTransient((provider) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("SqlServer");

            return new SqlConnection(connectionString);
        });

        return builder;
    }

    public static WebApplication UseBenchmarks(this WebApplication app)
    {
        app.MapGet("/ping", () => TypedResults.Text("pong"));

        app.MapGet("/httpclient", async (IServer server, HttpClient httpClient) =>
        {
            var serverAddresses = server.Features.Get<IServerAddressesFeature>();
            var baseAddress = serverAddresses!.Addresses.Select((p) => new Uri(p)).Last();
            var requestUri = new Uri(baseAddress, "/ping");

            var response = await httpClient.GetStringAsync(requestUri);

            return TypedResults.Text(response);
        });

        app.MapGet("/sqlserver/query", async (SqlConnection connection) =>
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES";

            var tables = new List<string>();

            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            return TypedResults.Ok(tables);
        });

        app.MapGet("/sqlserver/sproc", async (SqlConnection connection) =>
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = "sp_server_info";
            command.CommandType = CommandType.StoredProcedure;

            var serverInfo = new List<string>();

            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    serverInfo.Add($"ID={reader.GetInt32(0)} , NAME={reader.GetString(1)} , VALUE={reader.GetString(2)}");
                }
            }

            return TypedResults.Ok(serverInfo);
        });

        return app;
    }
}
