// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using BenchmarkDotNet.Running;
using OpenTelemetry.Benchmarks;

var summaries = BenchmarkSwitcher.FromAssembly(typeof(EventSourceBenchmarks).Assembly).Run(args: args);
return summaries.SelectMany(p => p.Reports).Any((p) => !p.Success) ? 1 : 0;
