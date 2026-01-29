namespace Quark.EventSourcing;

/// <summary>
///     Exception thrown when there's a version mismatch during event appending.
/// </summary>
public sealed class EventStoreConcurrencyException : Exception
{
    /// <summary>
    ///     Gets the expected version.
    /// </summary>
    public long ExpectedVersion { get; }

    /// <summary>
    ///     Gets the actual version in the store.
    /// </summary>
    public long ActualVersion { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventStoreConcurrencyException"/> class.
    /// </summary>
    /// <param name="expectedVersion">The expected version.</param>
    /// <param name="actualVersion">The actual version.</param>
    public EventStoreConcurrencyException(long expectedVersion, long actualVersion)
        : base($"Event store concurrency conflict: expected version {expectedVersion}, but actual version is {actualVersion}")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
