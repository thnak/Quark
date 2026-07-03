namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     DI singleton test double: a manually-releasable async gate used to prove ordering/blocking
///     structurally (a grain method or timer callback awaits it) instead of racing on wall-clock
///     delays.
/// </summary>
public sealed class Gate
{
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitAsync() => _tcs.Task;

    public void Release() => _tcs.TrySetResult();

    public void Reset() => _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
}
