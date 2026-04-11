namespace Quark.Serialization.Abstractions.Exceptions;

/// <summary>Thrown when serialization or deserialization fails.</summary>
public class SerializationException : Exception
{
    /// <inheritdoc/>
    public SerializationException() { }

    /// <inheritdoc/>
    public SerializationException(string message) : base(message) { }

    /// <inheritdoc/>
    public SerializationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Thrown when no codec is registered for a requested type.</summary>
public sealed class CodecNotFoundException : SerializationException
{
    /// <summary>The type for which no codec was found.</summary>
    public Type? MissingType { get; }

    /// <inheritdoc/>
    public CodecNotFoundException(Type type)
        : base($"No codec is registered for type '{type.FullName}'. " +
               "Annotate the type with [GenerateSerializer] or register a custom IFieldCodec<T>.")
    {
        MissingType = type;
    }
}
