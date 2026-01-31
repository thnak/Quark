namespace Quark.Placement.Locality;

/// <summary>
/// Represents communication metrics between two actors.
/// </summary>
public sealed class CommunicationMetrics
{
    /// <summary>
    /// Gets or sets the total number of messages exchanged.
    /// </summary>
    public long MessageCount { get; set; }

    /// <summary>
    /// Gets or sets the total bytes transferred.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets or sets the average latency for message delivery.
    /// </summary>
    public TimeSpan AverageLatency { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last recorded interaction.
    /// </summary>
    public DateTimeOffset LastInteraction { get; set; }
}

/// <summary>
/// Represents a pair of actors that communicate frequently.
/// </summary>
public sealed class ActorPair
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorPair"/> class.
    /// </summary>
    public ActorPair(string fromActorId, string toActorId, CommunicationMetrics metrics)
    {
        FromActorId = fromActorId ?? throw new ArgumentNullException(nameof(fromActorId));
        ToActorId = toActorId ?? throw new ArgumentNullException(nameof(toActorId));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Gets the source actor ID.
    /// </summary>
    public string FromActorId { get; }

    /// <summary>
    /// Gets the destination actor ID.
    /// </summary>
    public string ToActorId { get; }

    /// <summary>
    /// Gets the communication metrics between the actors.
    /// </summary>
    public CommunicationMetrics Metrics { get; }
}

/// <summary>
/// Represents a graph of actor communication patterns.
/// </summary>
public sealed class CommunicationGraph
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommunicationGraph"/> class.
    /// </summary>
    public CommunicationGraph()
    {
        Edges = new Dictionary<string, Dictionary<string, CommunicationMetrics>>();
    }

    /// <summary>
    /// Gets the edges in the communication graph.
    /// Key: fromActorId, Value: Dictionary of (toActorId -> metrics)
    /// </summary>
    public Dictionary<string, Dictionary<string, CommunicationMetrics>> Edges { get; }

    /// <summary>
    /// Adds or updates a communication edge in the graph.
    /// </summary>
    public void AddOrUpdateEdge(string fromActorId, string toActorId, long messageSize)
    {
        if (!Edges.TryGetValue(fromActorId, out var targets))
        {
            targets = new Dictionary<string, CommunicationMetrics>();
            Edges[fromActorId] = targets;
        }

        if (!targets.TryGetValue(toActorId, out var metrics))
        {
            metrics = new CommunicationMetrics
            {
                MessageCount = 0,
                TotalBytes = 0,
                AverageLatency = TimeSpan.Zero,
                LastInteraction = DateTimeOffset.UtcNow
            };
            targets[toActorId] = metrics;
        }

        metrics.MessageCount++;
        metrics.TotalBytes += messageSize;
        metrics.LastInteraction = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the communication metrics between two actors.
    /// </summary>
    public CommunicationMetrics? GetMetrics(string fromActorId, string toActorId)
    {
        if (Edges.TryGetValue(fromActorId, out var targets))
        {
            targets.TryGetValue(toActorId, out var metrics);
            return metrics;
        }
        return null;
    }
}
