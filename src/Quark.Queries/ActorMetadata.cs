namespace Quark.Queries;

/// <summary>
/// Metadata about an actor for querying and analytics.
/// </summary>
public sealed class ActorMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ActorMetadata"/> class.
    /// </summary>
    public ActorMetadata(
        string actorId,
        string actorType,
        string fullTypeName,
        bool isReentrant,
        bool isStateless,
        DateTimeOffset activatedAt,
        string? customName = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        FullTypeName = fullTypeName ?? throw new ArgumentNullException(nameof(fullTypeName));
        IsReentrant = isReentrant;
        IsStateless = isStateless;
        ActivatedAt = activatedAt;
        CustomName = customName;
    }

    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    public string ActorId { get; }

    /// <summary>
    /// Gets the actor type name (simple class name).
    /// </summary>
    public string ActorType { get; }

    /// <summary>
    /// Gets the full type name (including namespace).
    /// </summary>
    public string FullTypeName { get; }

    /// <summary>
    /// Gets whether the actor supports concurrent message processing.
    /// </summary>
    public bool IsReentrant { get; }

    /// <summary>
    /// Gets whether the actor is stateless (multiple instances per ID).
    /// </summary>
    public bool IsStateless { get; }

    /// <summary>
    /// Gets the timestamp when the actor was activated.
    /// </summary>
    public DateTimeOffset ActivatedAt { get; }

    /// <summary>
    /// Gets the custom name from ActorAttribute, if specified.
    /// </summary>
    public string? CustomName { get; }

    /// <summary>
    /// Gets or sets custom metadata properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}
