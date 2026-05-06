namespace Quark.Core.Abstractions.Exceptions;

/// <summary>Base exception type for all Quark framework errors.</summary>
public class QuarkException : Exception
{
    /// <inheritdoc/>
    public QuarkException() { }

    /// <inheritdoc/>
    public QuarkException(string message) : base(message) { }

    /// <inheritdoc/>
    public QuarkException(string message, Exception innerException) : base(message, innerException) { }
}