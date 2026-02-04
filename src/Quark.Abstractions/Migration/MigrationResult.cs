namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents the result of an actor migration operation.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MigrationResult"/> class.
    /// </summary>
    public MigrationResult(
        string actorId,
        string actorType,
        string sourceSiloId,
        string targetSiloId,
        MigrationStatus status,
        string? errorMessage = null)
    {
        ActorId = actorId ?? throw new ArgumentNullException(nameof(actorId));
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
        SourceSiloId = sourceSiloId ?? throw new ArgumentNullException(nameof(sourceSiloId));
        TargetSiloId = targetSiloId ?? throw new ArgumentNullException(nameof(targetSiloId));
        Status = status;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
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
    /// Gets the source silo ID.
    /// </summary>
    public string SourceSiloId { get; }

    /// <summary>
    /// Gets the target silo ID.
    /// </summary>
    public string TargetSiloId { get; }

    /// <summary>
    /// Gets the migration status.
    /// </summary>
    public MigrationStatus Status { get; }

    /// <summary>
    /// Gets the error message if migration failed.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the timestamp when migration completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>
    /// Gets whether the migration was successful.
    /// </summary>
    public bool IsSuccessful => Status == MigrationStatus.Completed;
}