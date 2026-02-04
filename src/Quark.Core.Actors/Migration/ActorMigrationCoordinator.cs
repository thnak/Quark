using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Abstractions.Persistence;
using Quark.Abstractions.Reminders;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Default implementation of IActorMigrationCoordinator.
/// Coordinates actor migration during rolling upgrades and rebalancing.
/// </summary>
public sealed class ActorMigrationCoordinator : IActorMigrationCoordinator
{
    private readonly IActorFactory _actorFactory;
    private readonly IReminderTable? _reminderTable;
    private readonly ILogger<ActorMigrationCoordinator> _logger;
    private readonly ConcurrentDictionary<string, MigrationState> _activeMigrations = new();
    private readonly ConcurrentDictionary<string, bool> _drainingActors = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorMigrationCoordinator"/> class.
    /// </summary>
    public ActorMigrationCoordinator(
        IActorFactory actorFactory,
        ILogger<ActorMigrationCoordinator> logger,
        IReminderTable? reminderTable = null)
    {
        _actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reminderTable = reminderTable;
    }

    /// <inheritdoc />
    public int ActiveMigrationCount => _activeMigrations.Count;

    /// <inheritdoc />
    public async Task<MigrationResult> MigrateActorAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting migration of actor {ActorId} of type {ActorType} to silo {TargetSiloId}",
            actorId, actorType, targetSiloId);

        var migrationState = new MigrationState(actorId, actorType, targetSiloId);
        if (!_activeMigrations.TryAdd(actorId, migrationState))
        {
            var error = "Actor is already being migrated";
            _logger.LogWarning("{Error}: {ActorId}", error, actorId);
            return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Failed, error);
        }

        try
        {
            migrationState.Status = MigrationStatus.InProgress;

            // Step 1: Begin drain (stop routing new messages)
            await BeginDrainAsync(actorId, cancellationToken);

            // Step 2: Wait for in-flight operations to complete
            var drained = await WaitForDrainCompletionAsync(actorId, TimeSpan.FromSeconds(30), cancellationToken);
            if (!drained)
            {
                migrationState.Status = MigrationStatus.Failed;
                migrationState.ErrorMessage = "Timeout waiting for drain completion";
                _logger.LogWarning("Migration failed for {ActorId}: drain timeout", actorId);
                return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Failed, migrationState.ErrorMessage);
            }

            // Step 3: Transfer state to target silo
            var stateTransferred = await TransferActorStateAsync(actorId, actorType, targetSiloId, cancellationToken);
            if (!stateTransferred)
            {
                migrationState.Status = MigrationStatus.Failed;
                migrationState.ErrorMessage = "Failed to transfer actor state";
                _logger.LogWarning("Migration failed for {ActorId}: state transfer failed", actorId);
                return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Failed, migrationState.ErrorMessage);
            }

            // Step 4: Activate on target silo
            var activated = await ActivateOnTargetAsync(actorId, actorType, targetSiloId, cancellationToken);
            if (!activated)
            {
                migrationState.Status = MigrationStatus.Failed;
                migrationState.ErrorMessage = "Failed to activate on target silo";
                _logger.LogWarning("Migration failed for {ActorId}: activation failed", actorId);
                return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Failed, migrationState.ErrorMessage);
            }

            // Step 5: Migrate reminders if any
            if (_reminderTable != null)
            {
                await MigrateRemindersAsync(actorId, targetSiloId, cancellationToken);
            }

            // Success
            migrationState.Status = MigrationStatus.Completed;
            _logger.LogInformation("Successfully migrated actor {ActorId} to silo {TargetSiloId}", actorId, targetSiloId);
            return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            migrationState.Status = MigrationStatus.Cancelled;
            _logger.LogWarning("Migration cancelled for {ActorId}", actorId);
            return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Cancelled, "Migration was cancelled");
        }
        catch (Exception ex)
        {
            migrationState.Status = MigrationStatus.Failed;
            migrationState.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Migration failed for {ActorId}", actorId);
            return new MigrationResult(actorId, actorType, "current-silo", targetSiloId, MigrationStatus.Failed, ex.Message);
        }
        finally
        {
            _activeMigrations.TryRemove(actorId, out _);
            _drainingActors.TryRemove(actorId, out _);
        }
    }

    /// <inheritdoc />
    public Task BeginDrainAsync(string actorId, CancellationToken cancellationToken = default)
    {
        _drainingActors.TryAdd(actorId, true);
        _logger.LogDebug("Actor {ActorId} marked as draining", actorId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> WaitForDrainCompletionAsync(
        string actorId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Waiting for drain completion for actor {ActorId} with timeout {Timeout}", actorId, timeout);

        // In a real implementation, this would check the mailbox and active call count
        // For now, we just wait a bit to simulate drain time
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            // Simulate waiting for operations to complete
            await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<bool> TransferActorStateAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Transferring state for actor {ActorId} to silo {TargetSiloId}", actorId, targetSiloId);

        // In a real implementation, this would:
        // 1. Load state with version from storage
        // 2. Send state to target silo
        // 3. Target silo saves state with version check
        // 4. Handle concurrency conflicts with rollback

        // For now, return success (state is already in persistent storage)
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ActivateOnTargetAsync(
        string actorId,
        string actorType,
        string targetSiloId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Activating actor {ActorId} on target silo {TargetSiloId}", actorId, targetSiloId);

        // In a real implementation, this would:
        // 1. Send activation request to target silo
        // 2. Target silo creates actor instance
        // 3. Actor loads state from storage
        // 4. Actor is ready to receive messages

        // For now, return success
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<MigrationStatus?> GetMigrationStatusAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (_activeMigrations.TryGetValue(actorId, out var state))
        {
            return Task.FromResult<MigrationStatus?>(state.Status);
        }

        return Task.FromResult<MigrationStatus?>(null);
    }

    /// <summary>
    /// Migrates reminders from current silo to target silo.
    /// </summary>
    private async Task MigrateRemindersAsync(string actorId, string targetSiloId, CancellationToken cancellationToken)
    {
        if (_reminderTable == null)
        {
            return;
        }

        _logger.LogDebug("Migrating reminders for actor {ActorId} to silo {TargetSiloId}", actorId, targetSiloId);

        // Get all reminders for this actor
        var reminders = await _reminderTable.GetRemindersAsync(actorId, cancellationToken);

        // Reminders are already in persistent storage, so they will be picked up by the target silo
        // based on consistent hashing. We just need to ensure they're registered.
        foreach (var reminder in reminders)
        {
            // Re-register to ensure consistency
            await _reminderTable.RegisterAsync(reminder, cancellationToken);
        }

        _logger.LogDebug("Migrated {Count} reminders for actor {ActorId}", reminders.Count, actorId);
    }
}
