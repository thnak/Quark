namespace Quark.Placement.Memory;

/// <summary>
/// Represents memory metrics for a silo.
/// </summary>
public sealed class MemoryMetrics
{
    /// <summary>
    /// Gets or sets the total memory used by the process in bytes.
    /// </summary>
    public long TotalMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the available memory in bytes.
    /// </summary>
    public long AvailableMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the memory pressure (0.0 to 1.0).
    /// 0.0 = no pressure, 1.0 = critical pressure.
    /// </summary>
    public double MemoryPressure { get; set; }

    /// <summary>
    /// Gets or sets the number of Gen0 GC collections.
    /// </summary>
    public int Gen0Collections { get; set; }

    /// <summary>
    /// Gets or sets the number of Gen1 GC collections.
    /// </summary>
    public int Gen1Collections { get; set; }

    /// <summary>
    /// Gets or sets the number of Gen2 GC collections.
    /// </summary>
    public int Gen2Collections { get; set; }

    /// <summary>
    /// Gets or sets the last GC pause duration.
    /// </summary>
    public TimeSpan LastGCPause { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when these metrics were collected.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}

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
