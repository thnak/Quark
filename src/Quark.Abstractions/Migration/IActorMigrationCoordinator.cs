namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents the status of an actor migration operation.
/// </summary>
public enum MigrationStatus
{
    /// <summary>
    /// Migration has not started yet.
    /// </summary>
    NotStarted,

    /// <summary>
    /// Migration is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Migration completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Migration failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Migration was cancelled.
    /// </summary>
    Cancelled
}

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

/// <summary>
/// Coordinates actor migration during rolling upgrades and rebalancing operations.
/// Part of Phase 10.1.1 (Zero Downtime and Rolling Upgrades - Live Actor Migration).
/// </summary>
public interface IActorMigrationCoordinator
{
    /// <summary>
    /// Initiates migration of an actor from the current silo to a target silo.
    /// Implements drain pattern: stops routing new messages to the actor while completing in-flight operations.
    /// </summary>
    /// <param name="actorId">The actor ID to migrate.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="targetSiloId">The target silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The migration result.</returns>
    Task<MigrationResult> MigrateActorAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an actor as draining, preventing new messages from being routed to it.
    /// Existing messages in the queue will continue to be processed.
    /// </summary>
    /// <param name="actorId">The actor ID to drain.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task BeginDrainAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for all in-flight operations for a draining actor to complete.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="timeout">Maximum time to wait for operations to complete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if all operations completed within the timeout, false otherwise.</returns>
    Task<bool> WaitForDrainCompletionAsync(
        string actorId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transfers the actor's state and queued messages to the target silo.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="targetSiloId">The target silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if transfer was successful, false otherwise.</returns>
    Task<bool> TransferActorStateAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the actor on the target silo after state transfer.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="targetSiloId">The target silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if activation was successful, false otherwise.</returns>
    Task<bool> ActivateOnTargetAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current migration status for an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The migration status, or null if the actor is not being migrated.</returns>
    Task<MigrationStatus?> GetMigrationStatusAsync(
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the number of actors currently being migrated.
    /// </summary>
    int ActiveMigrationCount { get; }
}
