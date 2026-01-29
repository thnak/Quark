namespace Quark.Abstractions.Persistence;

/// <summary>
///     Exception thrown when an optimistic concurrency conflict is detected.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrencyException"/> class.
    /// </summary>
    public ConcurrencyException()
        : base("A concurrency conflict was detected. The state was modified by another operation.")
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrencyException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrencyException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConcurrencyException"/> class with version information.
    /// </summary>
    /// <param name="expectedVersion">The expected version.</param>
    /// <param name="actualVersion">The actual version found in storage.</param>
    public ConcurrencyException(long expectedVersion, long actualVersion)
        : base($"Concurrency conflict: Expected version {expectedVersion}, but found version {actualVersion}.")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>
    ///     Gets the expected version.
    /// </summary>
    public long? ExpectedVersion { get; }

    /// <summary>
    ///     Gets the actual version found in storage.
    /// </summary>
    public long? ActualVersion { get; }
}
