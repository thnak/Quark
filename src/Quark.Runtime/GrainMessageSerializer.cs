using System.Buffers;
using Quark.Core.Abstractions.Identity;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Runtime;

/// <summary>
///     Serializes request/response payloads for transport-routed grain invocation.
/// </summary>
public sealed class GrainMessageSerializer
{
    /// <summary>Serializes a grain invocation request.</summary>
    public byte[] SerializeRequest(GrainInvocationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteString(request.GrainId.Type.Value);
        writer.WriteString(request.GrainId.Key);
        writer.WriteVarUInt32(request.MethodId);

        object?[] args = request.Arguments ?? [];
        writer.WriteVarUInt32((uint)args.Length);
        foreach (object? arg in args)
        {
            WriteValue(writer, arg);
        }

        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation request.</summary>
    public GrainInvocationRequest DeserializeRequest(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        GrainType grainType = new(reader.ReadString());
        string key = reader.ReadString();
        uint methodId = reader.ReadVarUInt32();
        uint argCount = reader.ReadVarUInt32();

        object?[] arguments = new object?[argCount];
        for (int i = 0; i < argCount; i++)
        {
            arguments[i] = ReadValue(reader);
        }

        return new GrainInvocationRequest(new GrainId(grainType, key), methodId, arguments);
    }

    /// <summary>Serializes a grain invocation response.</summary>
    public byte[] SerializeResponse(GrainInvocationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        ArrayBufferWriter<byte> buffer = new();
        CodecWriter writer = new(buffer);
        writer.WriteByte(response.Success ? (byte)1 : (byte)0);
        WriteValue(writer, response.Result);
        WriteValue(writer, response.Error);
        return buffer.WrittenMemory.ToArray();
    }

    /// <summary>Deserializes a grain invocation response.</summary>
    public GrainInvocationResponse DeserializeResponse(ReadOnlyMemory<byte> buffer)
    {
        CodecReader reader = new(buffer);
        bool success = reader.ReadByte() == 1;
        object? result = ReadValue(reader);
        string? error = ReadValue(reader) as string;
        return new GrainInvocationResponse(success, result, error);
    }

    private static void WriteValue(CodecWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteByte((byte)ValueKind.Null);
                break;
            case bool b:
                writer.WriteByte((byte)ValueKind.Boolean);
                writer.WriteByte(b ? (byte)1 : (byte)0);
                break;
            case int i32:
                writer.WriteByte((byte)ValueKind.Int32);
                writer.WriteInt32(i32);
                break;
            case uint u32:
                writer.WriteByte((byte)ValueKind.UInt32);
                writer.WriteVarUInt32(u32);
                break;
            case long i64:
                writer.WriteByte((byte)ValueKind.Int64);
                writer.WriteInt64(i64);
                break;
            case ulong u64:
                writer.WriteByte((byte)ValueKind.UInt64);
                writer.WriteVarUInt64(u64);
                break;
            case string s:
                writer.WriteByte((byte)ValueKind.String);
                writer.WriteString(s);
                break;
            case Guid guid:
                writer.WriteByte((byte)ValueKind.Guid);
                writer.WriteRaw(guid.ToByteArray());
                break;
            case byte[] bytes:
                writer.WriteByte((byte)ValueKind.ByteArray);
                writer.WriteBytes(bytes);
                break;
            case double dbl:
                writer.WriteByte((byte)ValueKind.Double);
                writer.WriteFixed64((ulong)BitConverter.DoubleToInt64Bits(dbl));
                break;
            case float sgl:
                writer.WriteByte((byte)ValueKind.Single);
                writer.WriteFixed32((uint)BitConverter.SingleToInt32Bits(sgl));
                break;
            case decimal dec:
                writer.WriteByte((byte)ValueKind.Decimal);
                foreach (int part in decimal.GetBits(dec))
                {
                    writer.WriteInt32(part);
                }

                break;
            default:
                throw new NotSupportedException(
                    $"The transport message serializer does not support values of type '{value.GetType().FullName}'.");
        }
    }

    private static object? ReadValue(CodecReader reader)
    {
        var kind = (ValueKind)reader.ReadByte();
        return kind switch
        {
            ValueKind.Null => null,
            ValueKind.Boolean => reader.ReadByte() == 1,
            ValueKind.Int32 => reader.ReadInt32(),
            ValueKind.UInt32 => reader.ReadVarUInt32(),
            ValueKind.Int64 => reader.ReadInt64(),
            ValueKind.UInt64 => reader.ReadVarUInt64(),
            ValueKind.String => reader.ReadString(),
            ValueKind.Guid => new Guid(reader.ReadRaw(16)),
            ValueKind.ByteArray => reader.ReadBytes(),
            ValueKind.Double => BitConverter.Int64BitsToDouble((long)reader.ReadFixed64()),
            ValueKind.Single => BitConverter.Int32BitsToSingle((int)reader.ReadFixed32()),
            ValueKind.Decimal => new decimal([
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()
            ]),
            _ => throw new NotSupportedException($"Unsupported serialized value kind '{kind}'.")
        };
    }

    private enum ValueKind : byte
    {
        Null = 0,
        Boolean = 1,
        Int32 = 2,
        UInt32 = 3,
        Int64 = 4,
        UInt64 = 5,
        String = 6,
        Guid = 7,
        ByteArray = 8,
        Double = 9,
        Single = 10,
        Decimal = 11
    }
}
