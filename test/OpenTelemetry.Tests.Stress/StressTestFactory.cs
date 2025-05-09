// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using CommandLine;

namespace OpenTelemetry.Tests.Stress;

#pragma warning disable CA1515 // Consider making public types internal
public static class StressTestFactory
#pragma warning restore CA1515 // Consider making public types internal
{
    public static int RunSynchronously<TStressTest>(string[] commandLineArguments)
        where TStressTest : StressTests<StressTestOptions>
    {
        return RunSynchronously<TStressTest, StressTestOptions>(commandLineArguments);
    }

    public static int RunSynchronously<TStressTest, TStressTestOptions>(string[] commandLineArguments)
        where TStressTest : StressTests<TStressTestOptions>
        where TStressTestOptions : StressTestOptions
    {
        return Parser.Default.ParseArguments<TStressTestOptions>(commandLineArguments)
            .MapResult(
                CreateStressTestAndRunSynchronously,
                _ => 1);

        static int CreateStressTestAndRunSynchronously(TStressTestOptions options)
        {
            using var stressTest = (TStressTest)Activator.CreateInstance(typeof(TStressTest), options)!;

            stressTest.RunSynchronously();

            return 0;
        }
    }
}
