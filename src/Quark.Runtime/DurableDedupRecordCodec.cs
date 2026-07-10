using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Runtime;

/// <summary>
///     Hand-written <see cref="IFieldCodec{T}" /> for <see cref="DurableDedupRecord" />.
///     Quark.Runtime does not reference <c>Quark.CodeGenerator</c> (its own wire types, e.g.
///     <c>GrainInvocationRequest</c>/<c>Response</c>, are hand-serialized too) — this mirrors the
///     shape the generator emits for a plain <c>[GenerateSerializer]</c> class so a named
///     <c>IGrainStorage</c> provider (e.g. Redis) can serialize the record via <c>ISerializer</c>.
/// </summary>
internal sealed class DurableDedupRecordCodec : IFieldCodec<DurableDedupRecord>
{
    private readonly ICodecProvider _codecs;

    public DurableDedupRecordCodec(ICodecProvider codecs) => _codecs = codecs;

    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, DurableDedupRecord value)
    {
        if (value is null)
        {
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }

        writer.WriteFieldHeader(fieldId, WireType.TagDelimited);
        _codecs.GetRequiredCodec<ulong>().WriteField(writer, 0u, typeof(ulong), value.ArgHash);
        _codecs.GetRequiredCodec<byte[]?>().WriteField(writer, 1u, typeof(byte[]), value.Payload);
        _codecs.GetRequiredCodec<long>().WriteField(writer, 2u, typeof(long), value.CreatedAtUtcTicks);
        writer.WriteFieldHeader(0u, WireType.EndTagDelimited);
    }

    public DurableDedupRecord ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended)
        {
            return default!;
        }

        var result = new DurableDedupRecord();
        Field f;
        while (!(f = reader.ReadFieldHeader()).IsEndObject)
        {
            switch ((int)f.FieldId)
            {
                case 0: result.ArgHash = _codecs.GetRequiredCodec<ulong>().ReadValue(reader, f); break;
                case 1: result.Payload = _codecs.GetRequiredCodec<byte[]?>().ReadValue(reader, f); break;
                case 2: result.CreatedAtUtcTicks = _codecs.GetRequiredCodec<long>().ReadValue(reader, f); break;
                default: SkipField(reader, f); break;
            }
        }

        return result;
    }

    private static void SkipField(CodecReader reader, Field field)
    {
        switch (field.WireType)
        {
            case WireType.VarInt:
                reader.ReadVarUInt64();
                break;
            case WireType.Fixed32:
                reader.ReadFixed32();
                break;
            case WireType.Fixed64:
                reader.ReadFixed64();
                break;
            case WireType.LengthPrefixed:
                reader.ReadBytes();
                break;
            case WireType.TagDelimited:
                Field nested;
                while (!(nested = reader.ReadFieldHeader()).IsEndObject)
                {
                    SkipField(reader, nested);
                }
                break;
            case WireType.Extended:
            case WireType.EndTagDelimited:
                break;
        }
    }
}
