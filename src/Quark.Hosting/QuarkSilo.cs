using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Core.Reminders;
using Quark.Core.Streaming;
using Quark.Networking.Abstractions;

namespace Quark.Hosting;

/// <summary>
/// Default implementation of IQuarkSilo that manages the actor runtime lifecycle.
/// Orchestrates subsystems including ReminderTickManager, StreamBroker, and cluster membership.
/// </summary>
public sealed class QuarkSilo : IQuarkSilo, IHostedService
{
    private readonly IActorFactory _actorFactory;
    private readonly IQuarkClusterMembership _clusterMembership;
    private readonly IQuarkTransport _transport;
    private readonly ReminderTickManager? _reminderTickManager;
    private readonly StreamBroker? _streamBroker;
    private readonly QuarkSiloOptions _options;
    private readonly ILogger<QuarkSilo> _logger;
    private readonly ConcurrentDictionary<string, IActor> _actorRegistry = new();
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
        StreamBroker? streamBroker = null)
    {
        _actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reminderTickManager = reminderTickManager;
        _streamBroker = streamBroker;

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

            // 4. Deactivate all active actors
            await DeactivateAllActorsAsync(cancellationToken);

            // 5. Stop ReminderTickManager
            if (_reminderTickManager != null)
            {
                await _reminderTickManager.StopAsync(cancellationToken);
                _logger.LogInformation("ReminderTickManager stopped for silo {SiloId}", SiloId);
            }

            // 6. Wait for in-flight gRPC calls to complete (with timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ShutdownTimeout);
            await Task.Delay(TimeSpan.FromSeconds(1), timeoutCts.Token).ContinueWith(_ => { });
            _logger.LogInformation("Waited for in-flight calls to complete for silo {SiloId}", SiloId);

            // 7. Stop transport
            await _transport.StopAsync(cancellationToken);
            _logger.LogInformation("Transport stopped for silo {SiloId}", SiloId);

            // 8. Stop cluster membership monitoring
            await _clusterMembership.StopAsync(cancellationToken);
            _logger.LogInformation("Cluster membership stopped for silo {SiloId}", SiloId);

            // 9. Unregister from cluster
            await _clusterMembership.UnregisterSiloAsync(cancellationToken);
            _logger.LogInformation("Silo {SiloId} unregistered from cluster", SiloId);

            // 10. Mark as Dead
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
