namespace Quark.Serialization.Abstractions;

/// <summary>
/// Core contract for a codec that can handle any type it recognises.
/// Register implementations with the serializer infrastructure to support new types.
/// </summary>
public interface IGeneralizedCodec
{
    /// <summary>Returns <c>true</c> if this codec can serialise/deserialise <paramref name="type"/>.</summary>
    bool IsSupportedType(Type type);

    /// <summary>
    /// Writes the field header and value for <paramref name="value"/> using <paramref name="writer"/>.
    /// </summary>
    void WriteField(CodecWriter writer, uint fieldId, Type expectedType, object? value);

    /// <summary>Reads a value whose field header has already been read.</summary>
    object? ReadValue(CodecReader reader, Field field);
}
