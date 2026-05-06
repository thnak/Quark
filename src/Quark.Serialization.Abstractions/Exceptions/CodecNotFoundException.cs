namespace Quark.Serialization.Abstractions.Exceptions;

/// <summary>Thrown when no codec is registered for a requested type.</summary>
public sealed class CodecNotFoundException : SerializationException
{
    /// <inheritdoc />
    public CodecNotFoundException(Type type)
        : base($"No codec is registered for type '{type.FullName}'. " +
               "Annotate the type with [GenerateSerializer] or register a custom IFieldCodec<T>.")
    {
        MissingType = type;
    }

    /// <summary>The type for which no codec was found.</summary>
    public Type? MissingType { get; }
}
