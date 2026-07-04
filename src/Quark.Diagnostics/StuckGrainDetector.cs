using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime;

namespace Quark.Diagnostics;

/// <summary>
///     Background service that polls active grain activations for two distinct hang shapes:
///     a single work item running longer than <see cref="DiagnosticOptions.StuckThreshold" />
///     (fires <see cref="IQuarkDiagnosticListener.OnMailboxStuck" />, resolved via
///     <see cref="IQuarkDiagnosticListener.OnMailboxStuckResolved" />), and a livelock — the
///     scheduler rescheduling an activation <see cref="DiagnosticOptions.StalledDrainThreshold" />
///     times in a row without a single drain pass making progress (fires
///     <see cref="IQuarkDiagnosticListener.OnSchedulerDrainStalled" />).
/// </summary>
public sealed class StuckGrainDetector : BackgroundService
{
    private readonly GrainActivationTable _activationTable;
    private readonly IQuarkDiagnosticListener _listener;
    private readonly ILogger<StuckGrainDetector> _logger;
    private readonly DiagnosticOptions _options;

    // grainId → Stopwatch ticks at which we first reported it as stuck
    private readonly Dictionary<GrainId, long> _stuckSince = new();

    // grainIds already reported as livelocked — reported once, not re-armed (see class doc).
    private readonly HashSet<GrainId> _reportedStalled = new();

    public StuckGrainDetector(
        GrainActivationTable activationTable,
        IQuarkDiagnosticListener listener,
        IOptions<DiagnosticOptions> options,
        ILogger<StuckGrainDetector> logger)
    {
        _activationTable = activationTable;
        _listener = listener;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.PollInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            PollOnce();
        }
    }

    /// <summary>
    ///     Runs a single poll pass synchronously. Split out of <see cref="ExecuteAsync" /> so tests
    ///     can drive the detector's logic directly instead of racing a real <see cref="PeriodicTimer" />
    ///     against ThreadPool contention from the rest of a parallel test run.
    /// </summary>
    internal void PollOnce()
    {
        long thresholdTicks = (long)(_options.StuckThreshold.TotalSeconds * Stopwatch.Frequency);
        long now = Stopwatch.GetTimestamp();
        var active = _activationTable.GetActiveActivations();

        var currentlyStuck = new HashSet<GrainId>();
        var currentlyActive = new HashSet<GrainId>();

        foreach ((GrainId grainId, GrainActivation activation) in active)
        {
            currentlyActive.Add(grainId);

            int consecutiveEmptyDrains = activation.ConsecutiveEmptyDrains;
            if (consecutiveEmptyDrains >= _options.StalledDrainThreshold && _reportedStalled.Add(grainId))
            {
                int pending = activation.PendingWorkCount;

                _logger.LogWarning(
                    "Grain {GrainId} scheduler drain appears livelocked: {Count} consecutive drain passes processed nothing with {Pending} items still queued.",
                    grainId, consecutiveEmptyDrains, pending);

                _listener.OnSchedulerDrainStalled(
                    new SchedulerDrainStalledEvent(grainId, consecutiveEmptyDrains, pending));
            }

            long started = activation.WorkItemStartedAt;
            if (started == 0) continue; // idle

            long elapsed = now - started;
            if (elapsed < thresholdTicks) continue;

            currentlyStuck.Add(grainId);

            if (!_stuckSince.ContainsKey(grainId))
            {
                // First time we see this grain as stuck — fire the event once.
                _stuckSince[grainId] = started;
                TimeSpan runningFor = Stopwatch.GetElapsedTime(started);
                int pending = activation.PendingWorkCount;

                _logger.LogWarning(
                    "Grain {GrainId} mailbox appears stuck: work item running for {Elapsed:g} with {Pending} items pending.",
                    grainId, runningFor, pending);

                _listener.OnMailboxStuck(new MailboxStuckEvent(grainId, runningFor, pending));
            }
        }

        // Grains that were stuck but are no longer — fire resolved.
        foreach (GrainId grainId in _stuckSince.Keys.ToArray())
        {
            if (currentlyStuck.Contains(grainId)) continue;

            long stuckAt = _stuckSince[grainId];
            _stuckSince.Remove(grainId);
            TimeSpan totalDuration = Stopwatch.GetElapsedTime(stuckAt);

            _logger.LogInformation(
                "Grain {GrainId} mailbox stuck condition resolved after {Duration:g}.",
                grainId, totalDuration);

            _listener.OnMailboxStuckResolved(new MailboxStuckResolvedEvent(grainId, totalDuration));
        }

        // Drop stalled-drain reports for activations no longer active (deactivated, or table
        // entry cleared) so a future activation reusing the same key can be reported again.
        _reportedStalled.RemoveWhere(grainId => !currentlyActive.Contains(grainId));
    }
}
