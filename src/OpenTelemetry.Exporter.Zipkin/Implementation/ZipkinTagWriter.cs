// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenTelemetry.Internal;

namespace OpenTelemetry.Exporter.Zipkin.Implementation;

internal sealed class ZipkinTagWriter : JsonStringArrayTagWriter<Utf8JsonWriter>
{
    public const int StackallocByteThreshold = 256;

    private ZipkinTagWriter()
    {
    }

    public static ZipkinTagWriter Instance { get; } = new();

    protected override void WriteIntegralTag(ref Utf8JsonWriter writer, string key, long value)
    {
        Span<byte> destination = stackalloc byte[StackallocByteThreshold];
        if (Utf8Formatter.TryFormat(value, destination, out var bytesWritten))
        {
            writer.WriteString(key, destination.Slice(0, bytesWritten));
        }
        else
        {
            writer.WriteString(key, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    protected override void WriteFloatingPointTag(ref Utf8JsonWriter writer, string key, double value)
    {
        Span<byte> destination = stackalloc byte[StackallocByteThreshold];
        if (Utf8Formatter.TryFormat(value, destination, out var bytesWritten))
        {
            writer.WriteString(key, destination.Slice(0, bytesWritten));
        }
        else
        {
            writer.WriteString(key, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    protected override void WriteBooleanTag(ref Utf8JsonWriter writer, string key, bool value)
        => writer.WriteString(key, value ? "true" : "false");

    protected override void WriteStringTag(ref Utf8JsonWriter writer, string key, ReadOnlySpan<char> value)
        => writer.WriteString(key, value);

    protected override void WriteArrayTag(ref Utf8JsonWriter writer, string key, ArraySegment<byte> arrayUtf8JsonBytes)
    {
        writer.WritePropertyName(key);
        writer.WriteStringValue(arrayUtf8JsonBytes);
    }

    protected override void OnUnsupportedTagDropped(
        string tagKey,
        string tagValueTypeFullName)
    {
        ZipkinExporterEventSource.Log.UnsupportedAttributeType(
            tagValueTypeFullName,
            tagKey);
    }

    protected override bool TryWriteEmptyTag(ref Utf8JsonWriter state, string key, object? value) => false;

    protected override void WriteKvListTag(ref Utf8JsonWriter writer, string key, IEnumerable<KeyValuePair<string, object?>> value, int? tagValueMaxLength)
    {
        // Zipkin tags are a map<string, string>, so the nested map is JSON-encoded and written as
        // a string value. The members of that nested object preserve native JSON typing (numbers,
        // booleans, arrays, sub-objects), per
        // https://github.com/open-telemetry/opentelemetry-specification/blob/v1.60.0/specification/common/README.md#maps
        // -- unlike this writer's own tag-level methods above, which always produce a string because
        // that is what Zipkin's own tags field requires.
        using var stream = new MemoryStream();
        var nestedWriter = new Utf8JsonWriter(stream);

        try
        {
            nestedWriter.WriteStartObject();

            foreach (var kvp in value)
            {
                NestedValueWriter.NestedValue nestedValue = default;
                if (NestedValueWriter.Instance.TryWriteTag(ref nestedValue, kvp.Key, kvp.Value, tagValueMaxLength))
                {
                    nestedWriter.WritePropertyName(kvp.Key);

                    if (nestedValue.Value == null)
                    {
                        nestedWriter.WriteNullValue();
                    }
                    else if (nestedValue.IsJsonLiteral)
                    {
#if NET
                        nestedWriter.WriteRawValue(nestedValue.Value);
#else
                        using var doc = JsonDocument.Parse(nestedValue.Value);
                        doc.RootElement.WriteTo(nestedWriter);
#endif
                    }
                    else
                    {
                        nestedWriter.WriteStringValue(nestedValue.Value);
                    }
                }
            }

            nestedWriter.WriteEndObject();
            nestedWriter.Flush();
        }
        finally
        {
            nestedWriter.Dispose();
        }

        var success = stream.TryGetBuffer(out var buffer);
        Debug.Assert(success, "success was false");

        writer.WritePropertyName(key);
        writer.WriteStringValue(buffer);
    }

    // Computes the JSON-array/map-element representation of a single AnyValue -- i.e. preserving
    // native JSON types for numbers/booleans/arrays/maps, and quoting only strings, byte arrays
    // (Base64-encoded), and non-finite floating point values -- for use when a map-valued
    // attribute's members are written into the nested JSON object built by WriteKvListTag above.
    // This is intentionally separate from ZipkinTagWriter itself, whose methods always produce a
    // string because that is what a top-level Zipkin tag value requires.
    private sealed class NestedValueWriter : JsonStringArrayTagWriter<NestedValueWriter.NestedValue>
    {
        private NestedValueWriter()
        {
        }

        public static NestedValueWriter Instance { get; } = new();

        protected override void WriteIntegralTag(ref NestedValue state, string key, long value)
        {
            state.Key = key;
            state.Value = value.ToString(CultureInfo.InvariantCulture);
            state.IsJsonLiteral = true;
        }

        protected override void WriteFloatingPointTag(ref NestedValue state, string key, double value)
        {
            state.Key = key;
            state.Value = value.ToString(CultureInfo.InvariantCulture);

            // JSON has no representation for NaN or infinity, so those are emitted as strings.
            state.IsJsonLiteral = !double.IsNaN(value) && !double.IsInfinity(value);
        }

        protected override void WriteBooleanTag(ref NestedValue state, string key, bool value)
        {
            state.Key = key;
            state.Value = value ? "true" : "false";
            state.IsJsonLiteral = true;
        }

        protected override void WriteStringTag(ref NestedValue state, string key, ReadOnlySpan<char> value)
        {
            state.Key = key;
            state.Value = value.ToString();
            state.IsJsonLiteral = false;
        }

        protected override void WriteArrayTag(ref NestedValue state, string key, ArraySegment<byte> arrayUtf8JsonBytes)
        {
            state.Key = key;
#if NET
            state.Value = Encoding.UTF8.GetString(arrayUtf8JsonBytes.Array!, arrayUtf8JsonBytes.Offset, arrayUtf8JsonBytes.Count);
#else
            state.Value = Encoding.UTF8.GetString(arrayUtf8JsonBytes.Array, arrayUtf8JsonBytes.Offset, arrayUtf8JsonBytes.Count);
#endif
            state.IsJsonLiteral = true;
        }

        protected override void OnUnsupportedTagDropped(string tagKey, string tagValueTypeFullName)
            => ZipkinExporterEventSource.Log.UnsupportedAttributeType(tagValueTypeFullName, tagKey);

        protected override bool TryWriteEmptyTag(ref NestedValue state, string key, object? value)
        {
            state.Key = key;
            state.Value = null;
            state.IsJsonLiteral = true;
            return true;
        }

        protected override void WriteKvListTag(ref NestedValue state, string key, IEnumerable<KeyValuePair<string, object?>> kvList, int? tagValueMaxLength)
        {
            using var stream = new MemoryStream();
            var writer = new Utf8JsonWriter(stream);

            try
            {
                writer.WriteStartObject();

                foreach (var kvp in kvList)
                {
                    NestedValue nestedValue = default;
                    if (this.TryWriteTag(ref nestedValue, kvp.Key, kvp.Value, tagValueMaxLength))
                    {
                        writer.WritePropertyName(kvp.Key);

                        if (nestedValue.Value == null)
                        {
                            writer.WriteNullValue();
                        }
                        else if (nestedValue.IsJsonLiteral)
                        {
#if NET
                            writer.WriteRawValue(nestedValue.Value);
#else
                            using var doc = JsonDocument.Parse(nestedValue.Value);
                            doc.RootElement.WriteTo(writer);
#endif
                        }
                        else
                        {
                            writer.WriteStringValue(nestedValue.Value);
                        }
                    }
                }

                writer.WriteEndObject();
                writer.Flush();
            }
            finally
            {
                writer.Dispose();
            }

            var success = stream.TryGetBuffer(out var buffer);
            Debug.Assert(success, "success was false");

            state.Key = key;
#if NET
            state.Value = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
#else
            state.Value = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
#endif
            state.IsJsonLiteral = true;
        }

        internal struct NestedValue
        {
            public string? Key;

            public string? Value;

            public bool IsJsonLiteral;
        }
    }
}
