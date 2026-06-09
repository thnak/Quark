using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime;

namespace Quark.Diagnostics;

/// <summary>
///     Background service that polls active grain activations and fires
///     <see cref="IQuarkDiagnosticListener.OnMailboxStuck" /> when a work item has been
///     running longer than <see cref="DiagnosticOptions.StuckThreshold" />.
///     Fires <see cref="IQuarkDiagnosticListener.OnMailboxStuckResolved" /> once the grain
///     becomes idle again.
/// </summary>
public sealed class StuckGrainDetector : BackgroundService
{
    private readonly GrainActivationTable _activationTable;
    private readonly IQuarkDiagnosticListener _listener;
    private readonly ILogger<StuckGrainDetector> _logger;
    private readonly DiagnosticOptions _options;

    // grainId → Stopwatch ticks at which we first reported it as stuck
    private readonly Dictionary<GrainId, long> _stuckSince = new();

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
        long thresholdTicks = (long)(_options.StuckThreshold.TotalSeconds * Stopwatch.Frequency);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            long now = Stopwatch.GetTimestamp();
            var active = _activationTable.GetActiveActivations();

            var currentlyStuck = new HashSet<GrainId>();

            foreach ((GrainId grainId, GrainActivation activation) in active)
            {
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
        }
    }
}
