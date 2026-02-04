namespace Quark.Placement.Memory;

/// <summary>
/// Represents memory information for an actor.
/// </summary>
public sealed class ActorMemoryInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorMemoryInfo"/> class.
    /// </summary>
    public ActorMemoryInfo(string actorId, string actorType, long memoryBytes)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        MemoryBytes = memoryBytes;
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type.
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the estimated memory usage in bytes.
    /// </summary>
    public long MemoryBytes { get; }

    /// <summary>
    /// Gets or sets the timestamp of this measurement.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}