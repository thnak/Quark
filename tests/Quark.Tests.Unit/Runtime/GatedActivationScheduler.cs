using Quark.Runtime;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     An <see cref="IActivationScheduler"/> that drops every schedule request until
///     <see cref="Open"/> is called, then delegates to <see cref="SimpleActivationScheduler"/>. For
///     tests that drive drain passes by hand: a real scheduler <c>Task.Run</c>s a drain on
///     <c>PostAsync</c>, and under a loaded ThreadPool that task can start after a manual
///     <c>CompleteDrain</c> releases the drain lock, process the queued item with a live token, and
///     reset <see cref="GrainActivation.ConsecutiveEmptyDrains"/> behind the test's back.
///     Call <see cref="Open"/> before disposing the activation — <c>DisposeAsync</c> posts its
///     deactivation through the scheduler and would otherwise wait forever.
/// </summary>
internal sealed class GatedActivationScheduler : IActivationScheduler
{
    private volatile bool _open;

    public void Open() => _open = true;

    public ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default)
        => _open ? SimpleActivationScheduler.Instance.ScheduleAsync(activation, cancellationToken) : ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
