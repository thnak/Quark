namespace Quark.Abstractions.Migration;

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
