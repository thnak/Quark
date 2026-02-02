using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Migration;
using Quark.Client;
using Quark.Core.Actors;
using Quark.Core.Reminders;
using Quark.Core.Streaming;
using Quark.Networking.Abstractions;

namespace Quark.Hosting;

/// <summary>
/// Default implementation of IQuarkSilo that manages the actor runtime lifecycle.
/// Orchestrates subsystems including ReminderTickManager, StreamBroker, and cluster membership.
/// Phase 10.1.1: Integrated with IActorMigrationCoordinator for graceful actor migration during shutdown.
/// </summary>
public sealed class QuarkSilo : IQuarkSilo, IHostedService
{
    private readonly IActorFactory _actorFactory;
    private readonly IQuarkClusterMembership _clusterMembership;
    private readonly IQuarkTransport _transport;
    private readonly ReminderTickManager? _reminderTickManager;
    private readonly StreamBroker? _streamBroker;
    private readonly IActorMigrationCoordinator? _migrationCoordinator;
    private readonly IActorActivityTracker? _activityTracker;
    private readonly IClusterClient? _clusterClient;
    private readonly QuarkSiloOptions _options;
    private readonly ILogger<QuarkSilo> _logger;
    private readonly ConcurrentDictionary<string, IActor> _actorRegistry = new();
    private readonly ConcurrentDictionary<string, ActorInvocationMailbox> _actorMailboxes = new();
    private readonly CancellationTokenSource _heartbeatCts = new();
    private Task? _heartbeatTask;
    private SiloInfo _siloInfo;
    private SiloStatus _status = SiloStatus.Joining;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuarkSilo"/> class.
    /// </summary>
    public QuarkSilo(
        IActorFactory actorFactory,
        IQuarkClusterMembership clusterMembership,
        IQuarkTransport transport,
        QuarkSiloOptions options,
        ILogger<QuarkSilo> logger,
        ReminderTickManager? reminderTickManager = null,
        StreamBroker? streamBroker = null,
        IActorMigrationCoordinator? migrationCoordinator = null,
        IActorActivityTracker? activityTracker = null,
        IClusterClient? clusterClient = null)
    {
        _actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reminderTickManager = reminderTickManager;
        _streamBroker = streamBroker;
        _migrationCoordinator = migrationCoordinator;
        _activityTracker = activityTracker;
        _clusterClient = clusterClient;

        var siloId = options.SiloId ?? Guid.NewGuid().ToString("N");
        _siloInfo = new SiloInfo(siloId, options.Address, options.Port, SiloStatus.Joining);
    }

    /// <inheritdoc />
    public string SiloId => _siloInfo.SiloId;

    /// <inheritdoc />
    public SiloStatus Status => _status;

    /// <inheritdoc />
    public SiloInfo SiloInfo => _siloInfo;

    /// <inheritdoc />
    public IActorFactory ActorFactory => _actorFactory;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Quark Silo {SiloId} on {Endpoint}", SiloId, _siloInfo.Endpoint);

        try
        {
            // 0. Subscribe to transport EnvelopeReceived event for incoming actor invocations
            _transport.EnvelopeReceived += OnEnvelopeReceived;
            _logger.LogDebug("Subscribed to transport EnvelopeReceived event for silo {SiloId}", SiloId);

            // 1. Start transport layer
            await _transport.StartAsync(cancellationToken);
            _logger.LogInformation("Transport started for silo {SiloId}", SiloId);

            // 2. Register in cluster membership with Joining status
            await _clusterMembership.RegisterSiloAsync(_siloInfo, cancellationToken);
            _logger.LogInformation("Silo {SiloId} registered in cluster with status Joining", SiloId);

            // 3. Start cluster membership monitoring
            await _clusterMembership.StartAsync(cancellationToken);
            _logger.LogInformation("Cluster membership started for silo {SiloId}", SiloId);

            // 4. Transition to Active status
            _status = SiloStatus.Active;
            await _clusterMembership.UpdateHeartbeatAsync(cancellationToken);
            _logger.LogInformation("Silo {SiloId} transitioned to Active", SiloId);

            // 5. Start ReminderTickManager if enabled
            if (_options.EnableReminders && _reminderTickManager != null)
            {
                await _reminderTickManager.StartAsync(cancellationToken);
                _logger.LogInformation("ReminderTickManager started for silo {SiloId}", SiloId);
            }

            // 6. Start heartbeat task
            _heartbeatTask = HeartbeatLoopAsync(_heartbeatCts.Token);
            
            // 7. Log active silos in cluster
            var activeSilos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
            _logger.LogInformation("Silo {SiloId} discovered {Count} active silos in cluster", SiloId, activeSilos.Count);

            // 8. Optionally connect cluster client for silo-internal operations
            if (_clusterClient != null)
            {
                await _clusterClient.ConnectAsync(cancellationToken);
                _logger.LogInformation("Cluster client connected for silo {SiloId}", SiloId);
            }
            
            _logger.LogInformation("Quark Silo {SiloId} started successfully", SiloId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Quark Silo {SiloId}", SiloId);
            _status = SiloStatus.Dead;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Quark Silo {SiloId}", SiloId);

        try
        {
            // 1. Mark silo as ShuttingDown
            _status = SiloStatus.ShuttingDown;
            await _clusterMembership.UpdateHeartbeatAsync(cancellationToken);
            _logger.LogInformation("Silo {SiloId} marked as ShuttingDown", SiloId);

            // 2. Stop heartbeat
            await _heartbeatCts.CancelAsync();
            if (_heartbeatTask != null)
            {
                await _heartbeatTask;
            }

            // 3. Stop accepting new actor activations (implicit - status check would prevent this)
            _logger.LogInformation("Stopped accepting new actor activations for silo {SiloId}", SiloId);

            // 3.5 Phase 10.1.1: Migrate cold actors to available silos (if migration enabled)
            if (_migrationCoordinator != null && _activityTracker != null && _options.EnableLiveMigration)
            {
                await MigrateColdActorsAsync(cancellationToken);
            }

            // 4. Stop all actor mailboxes (graceful shutdown)
            await StopAllMailboxesAsync(cancellationToken);

            // 5. Deactivate all active actors
            await DeactivateAllActorsAsync(cancellationToken);

            // 6. Stop ReminderTickManager
            if (_reminderTickManager != null)
            {
                await _reminderTickManager.StopAsync(cancellationToken);
                _logger.LogInformation("ReminderTickManager stopped for silo {SiloId}", SiloId);
            }

            // 7. Wait for in-flight gRPC calls to complete (with timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ShutdownTimeout);
            await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token).ContinueWith(_ => { });
            _logger.LogInformation("Waited for in-flight calls to complete for silo {SiloId}", SiloId);

            // 8. Unsubscribe from transport events
            _transport.EnvelopeReceived -= OnEnvelopeReceived;
            _logger.LogDebug("Unsubscribed from transport EnvelopeReceived event for silo {SiloId}", SiloId);

            // 9. Stop transport
            await _transport.StopAsync(cancellationToken);
            _logger.LogInformation("Transport stopped for silo {SiloId}", SiloId);

            // 10. Stop cluster membership monitoring
            await _clusterMembership.StopAsync(cancellationToken);
            _logger.LogInformation("Cluster membership stopped for silo {SiloId}", SiloId);

            // 11. Unregister from cluster
            await _clusterMembership.UnregisterSiloAsync(cancellationToken);
            _logger.LogInformation("Silo {SiloId} unregistered from cluster", SiloId);

            // 12. Mark as Dead
            _status = SiloStatus.Dead;

            _logger.LogInformation("Quark Silo {SiloId} stopped successfully", SiloId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping Quark Silo {SiloId}", SiloId);
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IActor> GetActiveActors()
    {
        return _actorRegistry.Values.ToList();
    }

    /// <summary>
    /// Registers an actor in the silo's actor registry.
    /// </summary>
    internal void RegisterActor(string actorId, IActor actor)
    {
        _actorRegistry.TryAdd(actorId, actor);
        _logger.LogDebug("Actor {ActorId} registered in silo {SiloId}", actorId, SiloId);
    }

    /// <summary>
    /// Unregisters an actor from the silo's actor registry.
    /// </summary>
    internal void UnregisterActor(string actorId)
    {
        _actorRegistry.TryRemove(actorId, out _);
        _logger.LogDebug("Actor {ActorId} unregistered from silo {SiloId}", actorId, SiloId);
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Heartbeat loop started for silo {SiloId}", SiloId);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken);
                await _clusterMembership.UpdateHeartbeatAsync(cancellationToken);
                _logger.LogTrace("Heartbeat sent for silo {SiloId}", SiloId);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error sending heartbeat for silo {SiloId}", SiloId);
            }
        }

        _logger.LogInformation("Heartbeat loop stopped for silo {SiloId}", SiloId);
    }

    private async Task StopAllMailboxesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {Count} actor mailboxes for silo {SiloId}", _actorMailboxes.Count, SiloId);

        var tasks = new List<Task>();
        foreach (var kvp in _actorMailboxes)
        {
            tasks.Add(StopMailboxAsync(kvp.Key, kvp.Value, cancellationToken));
        }

        await Task.WhenAll(tasks);
        _actorMailboxes.Clear();

        _logger.LogInformation("All actor mailboxes stopped for silo {SiloId}", SiloId);
    }

    private async Task StopMailboxAsync(string actorId, ActorInvocationMailbox mailbox, CancellationToken cancellationToken)
    {
        try
        {
            await mailbox.StopAsync(cancellationToken);
            mailbox.Dispose();
            _logger.LogDebug("Mailbox for actor {ActorId} stopped", actorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping mailbox for actor {ActorId}", actorId);
        }
    }

    private async Task DeactivateAllActorsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deactivating {Count} actors for silo {SiloId}", _actorRegistry.Count, SiloId);

        var tasks = new List<Task>();
        foreach (var kvp in _actorRegistry)
        {
            tasks.Add(DeactivateActorAsync(kvp.Key, kvp.Value, cancellationToken));
        }

        await Task.WhenAll(tasks);
        _actorRegistry.Clear();

        _logger.LogInformation("All actors deactivated for silo {SiloId}", SiloId);
    }

    private async Task DeactivateActorAsync(string actorId, IActor actor, CancellationToken cancellationToken)
    {
        try
        {
            // Call OnDeactivateAsync to allow actor to save state and perform cleanup
            await actor.OnDeactivateAsync(cancellationToken);

            // Dispose if the actor implements IDisposable
            if (actor is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _logger.LogDebug("Actor {ActorId} deactivated", actorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating actor {ActorId}", actorId);
        }
    }

    /// <summary>
    /// Phase 10.1.1: Migrates cold actors to available silos during graceful shutdown.
    /// Prioritizes cold (idle) actors for migration to minimize disruption.
    /// </summary>
    private async Task MigrateColdActorsAsync(CancellationToken cancellationToken)
    {
        if (_migrationCoordinator == null || _activityTracker == null)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Starting cold actor migration for silo {SiloId} shutdown", SiloId);

            // Get migration priority list (cold actors first)
            var actorMetrics = await _activityTracker.GetMigrationPriorityListAsync(cancellationToken);
            
            if (!actorMetrics.Any())
            {
                _logger.LogInformation("No actors to migrate for silo {SiloId}", SiloId);
                return;
            }

            // Get available target silos from cluster
            var availableSilos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
            var targetSilos = availableSilos
                .Where(s => s.SiloId != SiloId && s.Status == SiloStatus.Active)
                .ToList();

            if (!targetSilos.Any())
            {
                _logger.LogWarning("No available target silos for migration from silo {SiloId}", SiloId);
                return;
            }

            var maxConcurrentMigrations = _options.MaxConcurrentMigrations;
            var migrationTasks = new List<Task>();
            var migratedCount = 0;
            var failedCount = 0;

            // Migrate cold actors first (up to configured limit or timeout)
            foreach (var metrics in actorMetrics.Where(m => m.IsCold).Take(maxConcurrentMigrations))
            {
                // Round-robin target selection
                var targetSilo = targetSilos[migratedCount % targetSilos.Count];
                
                var migrationTask = Task.Run(async () =>
                {
                    try
                    {
                        _logger.LogDebug(
                            "Migrating cold actor {ActorId} (type: {ActorType}, score: {Score}) to silo {TargetSilo}",
                            metrics.ActorId, metrics.ActorType, metrics.ActivityScore, targetSilo.SiloId);

                        var result = await _migrationCoordinator.MigrateActorAsync(
                            metrics.ActorId,
                            metrics.ActorType,
                            targetSilo.SiloId,
                            cancellationToken);

                        if (result.IsSuccessful)
                        {
                            Interlocked.Increment(ref migratedCount);
                            _logger.LogInformation(
                                "Successfully migrated actor {ActorId} to silo {TargetSilo}",
                                metrics.ActorId, targetSilo.SiloId);
                        }
                        else
                        {
                            Interlocked.Increment(ref failedCount);
                            _logger.LogWarning(
                                "Failed to migrate actor {ActorId}: {Error}",
                                metrics.ActorId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failedCount);
                        _logger.LogError(ex, "Error migrating actor {ActorId}", metrics.ActorId);
                    }
                }, cancellationToken);

                migrationTasks.Add(migrationTask);
            }

            // Wait for migrations to complete (with timeout)
            var migrationTimeout = _options.MigrationTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(migrationTimeout);

            try
            {
                await Task.WhenAll(migrationTasks).WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "Actor migration timed out after {Timeout} seconds for silo {SiloId}",
                    migrationTimeout.TotalSeconds, SiloId);
            }

            _logger.LogInformation(
                "Completed actor migration for silo {SiloId}: {Migrated} migrated, {Failed} failed",
                SiloId, migratedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cold actor migration for silo {SiloId}", SiloId);
            // Don't throw - allow shutdown to continue even if migration fails
        }
    }

    /// <summary>
    /// Handles incoming envelope events from the transport layer.
    /// Posts messages to actor mailboxes for sequential processing.
    /// </summary>
    private void OnEnvelopeReceived(object? sender, Networking.Abstractions.QuarkEnvelope envelope)
    {
        try
        {
            // IMPORTANT: Only process incoming requests, not responses
            // Responses (with ResponsePayload or IsError) should not be reprocessed
            // This prevents infinite loops when SendResponse raises EnvelopeReceived
            if (envelope.ResponsePayload != null || envelope.IsError)
            {
                // This is a response, not a request - skip processing
                _logger.LogTrace(
                    "Skipping response envelope {MessageId} (IsError={IsError}, HasResponsePayload={HasPayload})",
                    envelope.MessageId, envelope.IsError, envelope.ResponsePayload != null);
                return;
            }
            
            _logger.LogTrace(
                "Received envelope {MessageId} for actor {ActorId} ({ActorType}.{MethodName})",
                envelope.MessageId, envelope.ActorId, envelope.ActorType, envelope.MethodName);

            // 1. Look up the dispatcher for this actor type
            var dispatcher = ActorMethodDispatcherRegistry.GetDispatcher(envelope.ActorType);
            if (dispatcher == null)
            {
                _logger.LogWarning(
                    "No dispatcher found for actor type {ActorType}. Message {MessageId} cannot be processed.",
                    envelope.ActorType, envelope.MessageId);
                
                var errorResponse = new Networking.Abstractions.QuarkEnvelope(
                    envelope.MessageId,
                    envelope.ActorId,
                    envelope.ActorType,
                    envelope.MethodName,
                    Array.Empty<byte>(),
                    envelope.CorrelationId)
                {
                    IsError = true,
                    ErrorMessage = $"No dispatcher registered for actor type '{envelope.ActorType}'"
                };
                
                _transport.SendResponse(errorResponse);
                return;
            }

            // 2. Get or create the actor instance (reflection-free)
            if (_actorFactory is not ActorFactory concreteFactory)
            {
                _logger.LogError("ActorFactory must be an instance of Quark.Core.Actors.ActorFactory");
                
                var errorResponse = new Networking.Abstractions.QuarkEnvelope(
                    envelope.MessageId,
                    envelope.ActorId,
                    envelope.ActorType,
                    envelope.MethodName,
                    Array.Empty<byte>(),
                    envelope.CorrelationId)
                {
                    IsError = true,
                    ErrorMessage = "Invalid ActorFactory implementation"
                };
                
                _transport.SendResponse(errorResponse);
                return;
            }

            var actor = concreteFactory.GetOrCreateActorByName(envelope.ActorType, envelope.ActorId);

            if (actor == null)
            {
                _logger.LogError(
                    "Failed to create actor {ActorId} of type {ActorType}",
                    envelope.ActorId, envelope.ActorType);
                
                var errorResponse = new Networking.Abstractions.QuarkEnvelope(
                    envelope.MessageId,
                    envelope.ActorId,
                    envelope.ActorType,
                    envelope.MethodName,
                    Array.Empty<byte>(),
                    envelope.CorrelationId)
                {
                    IsError = true,
                    ErrorMessage = $"Failed to create actor '{envelope.ActorId}' of type '{envelope.ActorType}'"
                };
                
                _transport.SendResponse(errorResponse);
                return;
            }

            // 3. Register actor in silo's registry (if not already registered)
            RegisterActor(envelope.ActorId, actor);

            // 4. Get or create mailbox for this actor
            var mailbox = _actorMailboxes.GetOrAdd(envelope.ActorId, actorId =>
            {
                var newMailbox = new ActorInvocationMailbox(actor, dispatcher, _transport, _logger);
                // Start the mailbox processing immediately
                _ = newMailbox.StartAsync();
                _logger.LogDebug("Created and started mailbox for actor {ActorId}", actorId);
                return newMailbox;
            });

            // 5. Post message to mailbox for sequential processing
            var message = new ActorEnvelopeMessage(envelope);
            
            // Fire and forget the posting (but log any failures)
            _ = Task.Run(async () =>
            {
                try
                {
                    var posted = await mailbox.PostAsync(message, CancellationToken.None);
                    
                    if (!posted)
                    {
                        _logger.LogWarning(
                            "Failed to post envelope {MessageId} to mailbox for actor {ActorId} (channel may be closed)",
                            envelope.MessageId, envelope.ActorId);
                            
                        var errorResponse = new Networking.Abstractions.QuarkEnvelope(
                            envelope.MessageId,
                            envelope.ActorId,
                            envelope.ActorType,
                            envelope.MethodName,
                            Array.Empty<byte>(),
                            envelope.CorrelationId)
                        {
                            IsError = true,
                            ErrorMessage = "Failed to post message to mailbox"
                        };
                        
                        _transport.SendResponse(errorResponse);
                    }
                    else
                    {
                        _logger.LogTrace(
                            "Envelope {MessageId} posted to mailbox for actor {ActorId}",
                            envelope.MessageId, envelope.ActorId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error posting message to mailbox for actor {ActorId}", envelope.ActorId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling envelope {MessageId} for actor {ActorId}",
                envelope.MessageId, envelope.ActorId);

            // Send error response
            var errorResponse = new Networking.Abstractions.QuarkEnvelope(
                envelope.MessageId,
                envelope.ActorId,
                envelope.ActorType,
                envelope.MethodName,
                Array.Empty<byte>(),
                envelope.CorrelationId)
            {
                IsError = true,
                ErrorMessage = ex.Message
            };

            _transport.SendResponse(errorResponse);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _heartbeatCts.Dispose();
        _transport.Dispose();
    }

    // IHostedService implementation
    Task IHostedService.StartAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);
}
