// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

// TODO:
// - gRPC endpoint
// - An endpoint that makes an Redis client call
// - Add custom logging, metrics and traces

using Benchmarks.App;

var builder = WebApplication.CreateBuilder(args);

builder.AddBenchmarks();

var app = builder.Build();

app.UseBenchmarks();

app.Run();
