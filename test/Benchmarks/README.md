# OpenTelemetry Benchmarks

Navigate to `./test/Benchmarks` directory and run the following command:

```sh
dotnet run -c Release -f net11.0 -- -m
```

[How to use console arguments](https://benchmarkdotnet.org/articles/guides/console-args.html)

- `-m` enables MemoryDiagnoser and prints memory statistics
- `-f` allows you to filter the benchmarks by their full name using glob patterns
  - Run all benchmarks in a class: `dotnet run -c Release -f net11.0 -- -f '*LogBenchmarks*'`
  - Run a single benchmark: `dotnet run -c Release -f net11.0 -- -f '*LogBenchmarks.OneProcessor'`

  ## Verified optimization results

  The following results compare the benchmark-only baseline `22a2976` with the
  optimized implementation `630bee5`. Both were measured by one invocation of the
  repository's comparison script, using its default job and memory diagnoser:

  ```powershell
  ./benchmark.ps1 `
      -Benchmarks @("*TraceparentParsingBenchmarks*", "*PrometheusHeadersParserBenchmarks*") `
      -Target 630bee5 -Baseline 22a2976 `
      -Runtimes @("net10.0") -EnableMemoryDiagnoser
  ```

  Environment: BenchmarkDotNet 0.15.8, Ubuntu 24.04.3 container, AMD EPYC 9V74,
  .NET 10.0.14, concurrent workstation GC, x64 RyuJIT with AVX-512 support.
  The host SDK was 11.0.100-rc.3.26522.148. BenchmarkDotNet 0.15.8 does not recognize
  the .NET 11 runtime, so the benchmark child processes explicitly targeted
  .NET 10. These results do not establish performance on .NET 11, .NET Framework,
  or other CPU architectures.

  The tables below are the actual BenchmarkDotNet GitHub-markdown output.
  Complete run artifacts are written under `BenchmarkDotNet.Artifacts/630bee5/`
  and `BenchmarkDotNet.Artifacts/22a2976/`.

  ### Traceparent parsing

  [`TraceContextPropagator`](../../src/OpenTelemetry.Api/Context/Propagation/TraceContextPropagator.cs)
  uses `SearchValues<char>` and `ContainsAnyExcept` (introduced in .NET 8) to
  validate lowercase hexadecimal identifiers without throwing. It then constructs
  the IDs from their original character spans, avoiding the previous scalar
  hex-to-byte decoding followed by byte-to-hex re-encoding. Older target
  frameworks retain their existing implementation.

  Valid parsing dropped from 63.43 ns to 39.725 ns (37.4% less time), and extraction
  through the public propagator dropped from 72.82 ns to 45.899 ns (37.0% less time).
  Both still allocate 144 B for the trace and span identifier strings. Invalid
  hexadecimal, uppercase hexadecimal, and zero-ID rejection also improved.
  Invalid-character scenarios place the character at the end of the trace ID;
  they are not measurements of every possible invalid input.

  **Baseline**

  | Method  | Scenario     | Mean     | Error    | StdDev   | Gen0   | Allocated |
  |-------- |------------- |---------:|---------:|---------:|-------:|----------:|
  | **Parse**   | **InvalidHex**   | **25.33 ns** | **0.070 ns** | **0.066 ns** |      **-** |         **-** |
  | Extract | InvalidHex   | 36.31 ns | 0.293 ns | 0.245 ns |      - |         - |
  | **Parse**   | **UppercaseHex** | **25.76 ns** | **0.326 ns** | **0.289 ns** |      **-** |         **-** |
  | Extract | UppercaseHex | 35.53 ns | 0.711 ns | 0.631 ns |      - |         - |
  | **Parse**   | **Valid**        | **63.43 ns** | **0.970 ns** | **0.860 ns** | **0.0086** |     **144 B** |
  | Extract | Valid        | 72.82 ns | 0.381 ns | 0.298 ns | 0.0086 |     144 B |
  | **Parse**   | **ZeroId**       | **23.76 ns** | **0.491 ns** | **0.504 ns** |      **-** |         **-** |
  | Extract | ZeroId       | 33.29 ns | 0.246 ns | 0.230 ns |      - |         - |

  **Optimized**

  | Method  | Scenario     | Mean      | Error     | StdDev    | Gen0   | Allocated |
  |-------- |------------- |----------:|----------:|----------:|-------:|----------:|
  | **Parse**   | **InvalidHex**   |  **5.587 ns** | **0.0791 ns** | **0.0740 ns** |      **-** |         **-** |
  | Extract | InvalidHex   | 12.968 ns | 0.2533 ns | 0.2918 ns |      - |         - |
  | **Parse**   | **UppercaseHex** |  **5.467 ns** | **0.0496 ns** | **0.0464 ns** |      **-** |         **-** |
  | Extract | UppercaseHex | 13.092 ns | 0.2471 ns | 0.2190 ns |      - |         - |
  | **Parse**   | **Valid**        | **39.725 ns** | **0.7532 ns** | **0.6677 ns** | **0.0086** |     **144 B** |
  | Extract | Valid        | 45.899 ns | 0.7144 ns | 1.0910 ns | 0.0086 |     144 B |
  | **Parse**   | **ZeroId**       |  **7.579 ns** | **0.1706 ns** | **0.1596 ns** |      **-** |         **-** |
  | Extract | ZeroId       | 14.773 ns | 0.1166 ns | 0.0974 ns |      - |         - |

  An exploratory replacement using the non-allocating span overload of
  `Convert.FromHexString` (introduced in .NET 9), an uppercase check, and a zero-byte
  check was discarded. Short-job valid parsing was 71.787 ns versus 68.60 ns for
  the scalar baseline, while invalid-hex parsing regressed from 25.46 ns to
  33.785 ns. This did not demonstrate a useful gain; the default-job tables above
  measure only the retained `SearchValues` implementation.

  ### Prometheus HttpListener Accept header negotiation

  [`PrometheusHeadersParser`](../../src/OpenTelemetry.Exporter.Prometheus.HttpListener/Internal/PrometheusHeadersParser.cs)
  now compares media types and escaping schemes directly as spans. It selects
  the highest-quality supported protocol as it parses, instead of allocating a
  list and a LINQ ordering/selection pipeline. Equal quality factors still select
  the first valid protocol. This change applies to the HttpListener exporter,
  not ASP.NET Core's separate typed-header negotiation implementation.

  Plain-text negotiation dropped from 134.31 ns to 16.40 ns (87.8% less time) and
  from 640 B to zero managed allocation. OpenMetrics negotiation dropped from
  704 B to 32 B, and the weighted multi-protocol header dropped from 1104 B to
  128 B (88.4% fewer allocated bytes). Remaining allocations are parsed `Version`
  instances. These are parser microbenchmarks, not whole-scrape throughput results.

  **Baseline**

  | Method    | Scenario    | Mean      | Error    | StdDev   | Gen0   | Allocated |
  |---------- |------------ |----------:|---------:|---------:|-------:|----------:|
  | **Negotiate** | **OpenMetrics** | **167.45 ns** | **3.340 ns** | **3.124 ns** | **0.0420** |     **704 B** |
  | **Negotiate** | **Text**        | **134.31 ns** | **2.717 ns** | **3.627 ns** | **0.0381** |     **640 B** |
  | **Negotiate** | **Unsupported** |  **87.73 ns** | **1.502 ns** | **1.405 ns** | **0.0343** |     **576 B** |
  | **Negotiate** | **Weighted**    | **612.73 ns** | **6.303 ns** | **5.263 ns** | **0.0658** |    **1104 B** |

  **Optimized**

  | Method    | Scenario    | Mean      | Error    | StdDev   | Gen0   | Allocated |
  |---------- |------------ |----------:|---------:|---------:|-------:|----------:|
  | **Negotiate** | **OpenMetrics** |  **65.56 ns** | **0.181 ns** | **0.151 ns** | **0.0019** |      **32 B** |
  | **Negotiate** | **Text**        |  **16.40 ns** | **0.379 ns** | **0.336 ns** |      **-** |         **-** |
  | **Negotiate** | **Unsupported** |  **12.12 ns** | **0.296 ns** | **0.247 ns** |      **-** |         **-** |
  | **Negotiate** | **Weighted**    | **392.29 ns** | **1.847 ns** | **1.543 ns** | **0.0076** |     **128 B** |
