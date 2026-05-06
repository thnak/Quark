namespace Quark.Core.Abstractions.Exceptions;

/// <summary>Thrown when a silo cannot be reached during a grain call.</summary>
public sealed class SiloUnavailableException : QuarkException
{
    /// <inheritdoc />
    public SiloUnavailableException() : base("The target silo is unavailable.")
    {
    }

    /// <inheritdoc />
    public SiloUnavailableException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public SiloUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
