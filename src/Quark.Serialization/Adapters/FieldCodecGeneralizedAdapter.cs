using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Adapters;

/// <summary>
///     Wraps a typed <see cref="IFieldCodec{T}" /> as an <see cref="IGeneralizedCodec" />
///     so the gateway can serialize stream items by runtime type without knowing <typeparamref name="T" />
///     at compile time.
/// </summary>
internal sealed class FieldCodecGeneralizedAdapter<T> : IGeneralizedCodec
{
    private readonly IFieldCodec<T> _codec;

    internal FieldCodecGeneralizedAdapter(IFieldCodec<T> codec) => _codec = codec;

    public bool IsSupportedType(Type type) => type == typeof(T);

    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, object? value)
        => _codec.WriteField(writer, fieldId, expectedType, value is T v ? v : default!);

    public object? ReadValue(CodecReader reader, Field field) => _codec.ReadValue(reader, field);
}
