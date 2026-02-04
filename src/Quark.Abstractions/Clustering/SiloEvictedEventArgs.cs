namespace Quark.Abstractions.Clustering;

/// <summary>
///     Event arguments for silo eviction events.
/// </summary>
public sealed class SiloEvictedEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SiloEvictedEventArgs" /> class.
    /// </summary>
    public SiloEvictedEventArgs(SiloInfo siloInfo, string reason)
    {
        SiloInfo = siloInfo ?? throw new ArgumentNullException(nameof(siloInfo));
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Gets the information about the evicted silo.
    /// </summary>
    public SiloInfo SiloInfo { get; }

    /// <summary>
    ///     Gets the reason for eviction.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    ///     Gets the timestamp when the eviction occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}