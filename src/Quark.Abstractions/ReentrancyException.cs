namespace Quark.Abstractions;

/// <summary>
///     Exception thrown when a circular dependency (reentrancy) is detected in the call chain.
/// </summary>
public sealed class ReentrancyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ReentrancyException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public ReentrancyException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReentrancyException" /> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ReentrancyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}