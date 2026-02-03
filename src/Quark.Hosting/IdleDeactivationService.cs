// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Abstractions.Migration;

namespace Quark.Hosting;

/// <summary>
/// Background service that automatically deactivates idle actors based on a deactivation policy.
/// Part of serverless actor hosting for auto-scaling from zero.
/// </summary>
public sealed class IdleDeactivationService : BackgroundService
{
    private readonly IQuarkSilo _silo;
    private readonly IActorActivityTracker _activityTracker;
    private readonly IActorDeactivationPolicy _deactivationPolicy;
    private readonly ServerlessActorOptions _options;
    private readonly ILogger<IdleDeactivationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdleDeactivationService"/> class.
    /// </summary>
    public IdleDeactivationService(
        IQuarkSilo silo,
        IActorActivityTracker activityTracker,
        IActorDeactivationPolicy deactivationPolicy,
        ServerlessActorOptions options,
        ILogger<IdleDeactivationService> logger)
    {
        _silo = silo ?? throw new ArgumentNullException(nameof(silo));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _deactivationPolicy = deactivationPolicy ?? throw new ArgumentNullException(nameof(deactivationPolicy));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Serverless auto-deactivation is disabled");
            return;
        }

        _logger.LogInformation(
            "Starting serverless idle deactivation service with {IdleTimeout} timeout, checking every {CheckInterval}",
            _options.IdleTimeout,
            _options.CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckInterval, stoppingToken);
                await CheckAndDeactivateIdleActorsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for idle actors");
            }
        }

        _logger.LogInformation("Stopped serverless idle deactivation service");
    }

    private async Task CheckAndDeactivateIdleActorsAsync(CancellationToken cancellationToken)
    {
        var activeActors = _silo.GetActiveActors();
        var totalActors = activeActors.Count;
        var deactivatedCount = 0;

        if (totalActors == 0)
        {
            return;
        }

        _logger.LogDebug("Checking {ActorCount} active actors for idle deactivation", totalActors);

        foreach (var actor in activeActors)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var metrics = await _activityTracker.GetActivityMetricsAsync(actor.ActorId, cancellationToken);
                if (metrics == null)
                {
                    continue;
                }

                // Check if we should deactivate based on policy
                if (_deactivationPolicy.ShouldDeactivate(
                        actor.ActorId,
                        metrics.ActorType,
                        metrics.LastActivityTime,
                        metrics.QueueDepth,
                        metrics.ActiveCallCount))
                {
                    // Respect minimum active actors setting
                    if (_options.MinimumActiveActors > 0 &&
                        (totalActors - deactivatedCount) <= _options.MinimumActiveActors)
                    {
                        _logger.LogDebug(
                            "Skipping deactivation of actor {ActorId} to maintain minimum active count of {MinCount}",
                            actor.ActorId,
                            _options.MinimumActiveActors);
                        continue;
                    }

                    _logger.LogInformation(
                        "Deactivating idle actor {ActorId} of type {ActorType} (idle for {IdleDuration})",
                        actor.ActorId,
                        metrics.ActorType,
                        DateTimeOffset.UtcNow - metrics.LastActivityTime);

                    // Call OnDeactivateAsync to allow cleanup
                    await actor.OnDeactivateAsync(cancellationToken);

                    // Dispose if implements IDisposable
                    if (actor is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }

                    deactivatedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating actor {ActorId}", actor.ActorId);
            }
        }

        if (deactivatedCount > 0)
        {
            _logger.LogInformation(
                "Deactivated {DeactivatedCount} idle actors out of {TotalCount} total",
                deactivatedCount,
                totalActors);
        }
    }
}