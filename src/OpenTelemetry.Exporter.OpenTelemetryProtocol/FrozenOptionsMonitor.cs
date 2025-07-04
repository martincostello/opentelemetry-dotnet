// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace OpenTelemetry.Exporter;

internal static class FrozenOptionsMonitor
{
    public static IOptionsMonitor<T> Create<T>(T options) => new OptionsMonitor<T>(options);

    private sealed class OptionsMonitor<T>(T options) : IOptionsMonitor<T>
    {
        public T CurrentValue => options;

        public T Get(string? name) => options;

        public IDisposable OnChange(Action<T, string> listener) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        internal static readonly NullDisposable Instance = new();

        public void Dispose()
        {
            // No-op
        }
    }
}
