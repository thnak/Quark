using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class TimerLifecycleBehavior : IActivationLifecycle, ITimerLifecycleGrain
{
    private readonly IActivationMemory<TimerLifecycleState> _memory;
    private readonly IActivationShellAccessor _shell;
    private readonly IManagedActivationMemory<TrackedResource> _resource;

    public TimerLifecycleBehavior(
        IActivationMemory<TimerLifecycleState> memory,
        IActivationShellAccessor shell,
        IManagedActivationMemory<TrackedResource> resource,
        DeactivationTracker tracker)
    {
        _memory = memory;
        _shell = shell;
        _resource = resource
            .Init(() => ValueTask.FromResult(new TrackedResource()))
            .Destroy(_ =>
            {
                tracker.ManagedHolderDestroyed = true;
                return ValueTask.CompletedTask;
            });
    }

    public Task StartTimerAsync(bool timerThrows)
    {
        _memory.Value.Timer = _shell.Shell.RegisterTimer<bool>(
            (throwing, _) =>
            {
                _memory.Value.FireCount++;
                if (throwing)
                {
                    throw new InvalidOperationException("Simulated timer callback failure.");
                }
                return Task.CompletedTask;
            },
            timerThrows,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(10),
                Period = TimeSpan.FromMilliseconds(10),
            });
        return Task.CompletedTask;
    }

    public Task<int> GetFireCountAsync() => Task.FromResult(_memory.Value.FireCount);

    public Task ThrowAsync(string message) => throw new InvalidOperationException(message);

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }

    // Forces the managed resource to initialize, so its Destroy callback has something to
    // clean up on deactivation — GetAsync() is otherwise never called by these tests.
    public async Task OnActivateAsync(CancellationToken ct) => await _resource.GetAsync(ct);

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        => throw new InvalidOperationException("Simulated OnDeactivateAsync failure.");
}
