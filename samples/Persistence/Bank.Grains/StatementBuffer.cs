namespace Bank.Grains;

/// <summary>
///     The managed resource for <see cref="StatementBehavior" />: an in-memory buffer that stands in
///     for an expensive, async-to-open handle (a file writer, a pooled connection, a cached
///     projection…). It is created lazily on first use and flushed on deactivation. Never persisted.
/// </summary>
public sealed class StatementBuffer
{
    /// <summary>How many times the factory built this buffer — proves init runs once per activation.</summary>
    public int InitCount { get; set; }

    public List<string> Lines { get; } = [];
}
