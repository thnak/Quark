using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Regression coverage for a deactivation-during-shutdown hang. Once
///     <see cref="ActivationScheduler.DisposeAsync"/> has begun, its drain workers exit, so any
///     activation scheduled afterwards would never be drained. <see cref="GrainActivation.DisposeAsync"/>
///     posts its own <c>OnDeactivate</c> turn through the scheduler during teardown and then awaits that
///     drain, so a silently-accepted-but-never-drained enqueue hangs the poster forever.
///     <para>
///         The earlier <c>Channel&lt;GrainActivation&gt;</c>-based ready queue made
///         <c>ScheduleAsync</c> throw <c>ChannelClosedException</c> once the channel completed on
///         shutdown, which drove the activation's inline-drain fallback
///         (<c>GrainActivation.DrainDirectlyAndDeactivateAsync</c>). The <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/>
///         that replaced it has no closed state, so the reject-on-shutdown contract is now enforced
///         explicitly by <see cref="ActivationScheduler.ScheduleAsync"/>. These tests lock that contract
///         in: the surfaced hang was invisible to the old test suite because a since-removed
///         <c>SemaphoreSlim</c> park primitive happened to make the post-dispose wake throw
///         <see cref="ObjectDisposedException"/> for an unrelated reason (a disposed semaphore), masking
///         the missing guard.
///     </para>
/// </summary>
public sealed class ActivationSchedulerShutdownRejectsScheduleTests
{
    [Fact]
    public async Task ScheduleAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var options = new SiloRuntimeOptions { SchedulerMaxConcurrentActivations = 2 };
        var scheduler = new ActivationScheduler(options);

        var services = new ServiceCollection();
        await using ServiceProvider provider = services.BuildServiceProvider();

        var grainType = new GrainType("ShutdownRejectProbe");
        var activation = new GrainActivation(
            new GrainId(grainType, "g"), grainType, isReentrant: false,
            provider, NullLogger<GrainActivation>.Instance, scheduler);

        await scheduler.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await scheduler.ScheduleAsync(activation));
    }

    [Fact]
    public async Task ActivationDisposeAsync_AfterSchedulerDisposed_CompletesWithoutHanging()
    {
        var options = new SiloRuntimeOptions { SchedulerMaxConcurrentActivations = 2 };
        var scheduler = new ActivationScheduler(options);

        var services = new ServiceCollection();
        await using ServiceProvider provider = services.BuildServiceProvider();

        var grainType = new GrainType("ShutdownDeactivateProbe");
        var activation = new GrainActivation(
            new GrainId(grainType, "g"), grainType, isReentrant: false,
            provider, NullLogger<GrainActivation>.Instance, scheduler);

        // Drive one real turn so the activation is genuinely live on the scheduler before shutdown.
        await activation.PostAsync(() => ValueTask.CompletedTask);

        // Scheduler goes down first (mirrors the DI dispose order the runtime uses): its workers exit.
        await scheduler.DisposeAsync();

        // The activation is disposed afterwards (as GrainActivationTable does). Its OnDeactivate turn can
        // no longer be scheduler-driven, so it must fall back to draining inline and complete promptly --
        // never block awaiting a drain the dead scheduler will never perform.
        Task disposeActivation = activation.DisposeAsync().AsTask();
        Task completed = await Task.WhenAny(disposeActivation, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(disposeActivation, completed); // would time out (hang) before the ScheduleAsync guard
        await disposeActivation; // observe any fault
    }
}
