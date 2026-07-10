using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Regression coverage for the diagnostics gap found alongside the reentrancy deadlock in
///     <c>ActivationScheduler.DisposeAsync</c>: a hung drain worker previously blocked host
///     shutdown indefinitely with no observable signal. This verifies the shutdown watchdog fires
///     while a worker is still draining, without abandoning the real wait.
/// </summary>
public sealed class ActivationSchedulerShutdownWatchdogTests
{
    [Fact]
    public async Task DisposeAsync_FiresOnSchedulerShutdownStalled_WhileAWorkerIsStillDraining()
    {
        var listener = new RecordingListener();
        var diagnosticOptions = new DiagnosticOptions { ShutdownStalledThreshold = TimeSpan.FromMilliseconds(50) };
        var siloOptions = new SiloRuntimeOptions { SchedulerMaxConcurrentActivations = 1 };
        var scheduler = new ActivationScheduler(siloOptions, listener, diagnosticOptions);

        var services = new ServiceCollection();
        await using ServiceProvider provider = services.BuildServiceProvider();

        var grainType = new GrainType("ShutdownWatchdogProbe");
        var activation = new GrainActivation(
            new GrainId(grainType, "g"), grainType, isReentrant: false,
            provider, NullLogger<GrainActivation>.Instance, scheduler);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = activation.PostAsync(async () =>
        {
            started.SetResult();
            await gate.Task; // holds the scheduler's only worker mid-drain until released below.
        });
        await started.Task;

        Task disposeTask = scheduler.DisposeAsync().AsTask();
        try
        {
            await WaitForAsync(() => listener.ShutdownStalledEvents.Count > 0);
        }
        finally
        {
            gate.SetResult(); // let the worker finish so DisposeAsync (and this test) can complete.
        }

        await disposeTask;

        SchedulerShutdownStalledEvent fired = Assert.Single(listener.ShutdownStalledEvents);
        Assert.Equal(1, fired.TotalWorkerCount);
        Assert.True(fired.PendingWorkerCount >= 1);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    private sealed class RecordingListener : IQuarkDiagnosticListener
    {
        public List<SchedulerShutdownStalledEvent> ShutdownStalledEvents { get; } = new();

        public void OnSchedulerShutdownStalled(in SchedulerShutdownStalledEvent e) => ShutdownStalledEvents.Add(e);
    }
}
