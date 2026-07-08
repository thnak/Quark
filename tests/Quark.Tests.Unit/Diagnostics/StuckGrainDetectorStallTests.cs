using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Diagnostics;

/// <summary>
///     Regression coverage for the diagnostics gap found alongside the drain-livelock bug:
///     <see cref="StuckGrainDetector"/> only ever watched a single work item running too long
///     (<see cref="IQuarkDiagnosticListener.OnMailboxStuck"/>). A livelocked activation never
///     starts a work item at all — it keeps getting rescheduled without ever draining anything —
///     so that check alone would never have caught it. This exercises the new
///     <see cref="IQuarkDiagnosticListener.OnSchedulerDrainStalled"/> signal end to end.
/// </summary>
public sealed class StuckGrainDetectorStallTests
{
    private static readonly GrainType Type = new("StallProbe");

    [Fact]
    public async Task ExecuteAsync_FiresOnSchedulerDrainStalled_WhenActivationLivelocked()
    {
        var table = new GrainActivationTable(NullLogger<GrainActivationTable>.Instance);
        var grainId = new GrainId(Type, "stalled");
        var activation = new GrainActivation(
            grainId, Type, isReentrant: false,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<GrainActivation>.Instance);
        activation.MarkActive();
        await table.GetOrCreateAsync(grainId, () => ValueTask.FromResult(activation));

        // Manufacture the livelock shape directly: queue work, then drain repeatedly with an
        // already-cancelled token so DrainAsync never actually processes it (TryRead
        // short-circuits on cancellation while TryPeek — which ignores it — keeps reporting more
        // work), the same signature the real bug produced.
        Assert.True(activation.TryBeginDrain());
        _ = activation.PostAsync(() => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        for (int i = 0; i < 3; i++)
        {
            if (i > 0) Assert.True(activation.TryBeginDrain());
            ActivationDrainResult result = await activation.DrainAsync(int.MaxValue, cts.Token);
            activation.CompleteDrain(result);
        }

        Assert.True(activation.ConsecutiveEmptyDrains >= 3);

        var listener = new RecordingListener();
        IOptions<DiagnosticOptions> options = Options.Create(new DiagnosticOptions { StalledDrainThreshold = 3 });
        var detector = new StuckGrainDetector(table, listener, options, NullLogger<StuckGrainDetector>.Instance);

        // Call the poll step directly instead of starting the BackgroundService and waiting on its
        // real PeriodicTimer: under a full parallel test run, ThreadPool contention could delay that
        // timer's callback past a fixed wall-clock assertion window, making the test flaky for a
        // reason unrelated to the detector's actual logic (see DrainStallDetectionTests for the same
        // "avoid wall-clock races" approach applied to GrainActivation).
        detector.PollOnce();

        SchedulerDrainStalledEvent fired = Assert.Single(listener.StalledEvents);
        Assert.Equal(grainId, fired.GrainId);
        Assert.True(fired.ConsecutiveEmptyDrains >= 3);
    }

    private sealed class RecordingListener : IQuarkDiagnosticListener
    {
        public List<SchedulerDrainStalledEvent> StalledEvents { get; } = new();

        public void OnSchedulerDrainStalled(in SchedulerDrainStalledEvent e) => StalledEvents.Add(e);
    }
}
