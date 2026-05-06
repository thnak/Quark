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