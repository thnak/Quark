using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
///     Typed field codec.  Each type that participates in Quark serialization has exactly one
///     implementation of <see cref="IFieldCodec{T}" /> (generated or hand-written).
/// </summary>
/// <typeparam name="T">The type handled by this codec.</typeparam>
public interface IFieldCodec<T>
{
    /// <summary>Writes a single field (header + value) to <paramref name="writer" />.</summary>
    void WriteField(CodecWriter writer, uint fieldId, Type expectedType, T value);

    /// <summary>Reads a field value whose header has already been consumed.</summary>
    T ReadValue(CodecReader reader, Field field);
}
