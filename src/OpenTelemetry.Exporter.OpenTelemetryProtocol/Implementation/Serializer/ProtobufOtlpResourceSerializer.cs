// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Buffers;
using System.Runtime.CompilerServices;
using OpenTelemetry.Resources;

namespace OpenTelemetry.Exporter.OpenTelemetryProtocol.Implementation.Serializer;

internal static class ProtobufOtlpResourceSerializer
{
    private const int ReserveSizeForLength = 4;
    private const int InitialBufferSize = 2048;

#if NETFRAMEWORK || NETSTANDARD2_0
    private static readonly ConditionalWeakTable<Resource, byte[]> CachedResourceBytes = new();
#else
    private static readonly ConditionalWeakTable<Resource, byte[]> CachedResourceBytes = [];
#endif

    private static ReadOnlySpan<byte> EmptyResourceBytes => [0x0A, 0x80, 0x80, 0x80, 0x00];

    internal static int WriteResource(byte[] buffer, int writePosition, Resource? resource)
    {
        if (resource == null || resource == Resource.Empty)
        {
            EmptyResourceBytes.CopyTo(buffer.AsSpan(writePosition));
            return writePosition + EmptyResourceBytes.Length;
        }

        var cached = CachedResourceBytes.GetOrAdd(resource, SerializeResourceToBytes);
        Buffer.BlockCopy(cached, 0, buffer, writePosition, cached.Length);
        return writePosition + cached.Length;
    }

    private static byte[] SerializeResourceToBytes(Resource resource)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(InitialBufferSize);

        try
        {
            while (true)
            {
                try
                {
                    var length = WriteResourceCore(buffer, 0, resource);
                    return buffer.AsSpan(0, length).ToArray();
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException)
                {
                    ProtobufSerializer.ReturnBuffer(pool, buffer);
                    buffer = pool.Rent(buffer.Length * 2);
                }
            }
        }
        finally
        {
            ProtobufSerializer.ReturnBuffer(pool, buffer);
        }
    }

    private static int WriteResourceCore(byte[] buffer, int writePosition, Resource resource)
    {
        var otlpTagWriterState = new ProtobufOtlpTagWriter.OtlpTagWriterState
        {
            Buffer = buffer,
            WritePosition = writePosition,
        };

        otlpTagWriterState.WritePosition = ProtobufSerializer.WriteTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.ResourceSpans_Resource, ProtobufWireType.LEN);
        var resourceLengthPosition = otlpTagWriterState.WritePosition;
        otlpTagWriterState.WritePosition += ReserveSizeForLength;

        if (resource.Attributes is IReadOnlyList<KeyValuePair<string, object>> resourceAttributesList)
        {
            for (var i = 0; i < resourceAttributesList.Count; i++)
            {
                ProcessResourceAttribute(ref otlpTagWriterState, resourceAttributesList[i]);
            }
        }
        else
        {
            foreach (var attribute in resource.Attributes)
            {
                ProcessResourceAttribute(ref otlpTagWriterState, attribute);
            }
        }

        for (var i = 0; i < resource.Entities.Count; i++)
        {
            WriteEntityRef(ref otlpTagWriterState, resource.Entities[i]);
        }

        var resourceLength = otlpTagWriterState.WritePosition - (resourceLengthPosition + ReserveSizeForLength);
        ProtobufSerializer.WriteReservedLength(otlpTagWriterState.Buffer, resourceLengthPosition, resourceLength);

        return otlpTagWriterState.WritePosition;
    }

    private static void ProcessResourceAttribute(ref ProtobufOtlpTagWriter.OtlpTagWriterState otlpTagWriterState, KeyValuePair<string, object> attribute) =>
        _ = ProtobufOtlpTagWriter.WriteKeyValue(
            ref otlpTagWriterState,
            ProtobufOtlpTraceFieldNumberConstants.Resource_Attributes,
            attribute.Key,
            attribute.Value);

    private static void WriteEntityRef(ref ProtobufOtlpTagWriter.OtlpTagWriterState otlpTagWriterState, Entity entity)
    {
        otlpTagWriterState.WritePosition = ProtobufSerializer.WriteTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.Resource_Entity_Refs, ProtobufWireType.LEN);
        var entityRefLengthPosition = otlpTagWriterState.WritePosition;
        otlpTagWriterState.WritePosition += ReserveSizeForLength;

        if (entity.SchemaUrl != null)
        {
            otlpTagWriterState.WritePosition = ProtobufSerializer.WriteStringWithTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.EntityRef_Schema_Url, entity.SchemaUrl);
        }

        otlpTagWriterState.WritePosition = ProtobufSerializer.WriteStringWithTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.EntityRef_Type, entity.Type);

        for (var i = 0; i < entity.IdentifyingAttributes.Count; i++)
        {
            otlpTagWriterState.WritePosition = ProtobufSerializer.WriteStringWithTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.EntityRef_Id_Keys, entity.IdentifyingAttributes[i].Key);
        }

        for (var i = 0; i < entity.DescriptiveAttributes.Count; i++)
        {
            otlpTagWriterState.WritePosition = ProtobufSerializer.WriteStringWithTag(otlpTagWriterState.Buffer, otlpTagWriterState.WritePosition, ProtobufOtlpTraceFieldNumberConstants.EntityRef_Description_Keys, entity.DescriptiveAttributes[i].Key);
        }

        var entityRefLength = otlpTagWriterState.WritePosition - (entityRefLengthPosition + ReserveSizeForLength);
        ProtobufSerializer.WriteReservedLength(otlpTagWriterState.Buffer, entityRefLengthPosition, entityRefLength);
    }
}
