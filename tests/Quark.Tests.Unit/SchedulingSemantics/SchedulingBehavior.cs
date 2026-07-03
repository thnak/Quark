using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;

namespace Quark.Tests.Unit.SchedulingSemantics;

public sealed class SchedulingBehavior : IActivationLifecycle, ISchedulingGrain
{
    private readonly IActivationMemory<SchedulingState> _memory;
    private readonly IActivationShellAccessor _shell;
    private readonly Gate _gate;
    private readonly EntryLog _entryLog;

    public SchedulingBehavior(
        IActivationMemory<SchedulingState> memory,
        IActivationShellAccessor shell,
        Gate gate,
        EntryLog entryLog)
    {
        _memory = memory;
        _shell = shell;
        _gate = gate;
        _entryLog = entryLog;
    }

    public Task RecordAsync(int index)
    {
        _memory.Value.Order.Add(index);
        return Task.CompletedTask;
    }

    public Task<int[]> GetOrderAsync() => Task.FromResult(_memory.Value.Order.ToArray());

    public async Task BlockThenRecordAsync(int index)
    {
        _entryLog.Record(index);
        await _gate.WaitAsync();
        _memory.Value.Order.Add(index);
    }

    public Task NoOpAsync() => Task.CompletedTask;

    public Task StartTimerAsync(bool interleave)
    {
        _shell.Shell.RegisterTimer<bool>(
            async (_, _) =>
            {
                int n = ++_memory.Value.TimerFireCount;
                if (n == 1)
                {
                    // Only the first tick blocks — subsequent ticks (if posted at all) complete
                    // immediately once dequeued, so the fire count reveals whether they were
                    // suppressed (Interleave=false) or queued behind the first (Interleave=true).
                    await _gate.WaitAsync();
                }
            },
            interleave,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(10),
                Period = TimeSpan.FromMilliseconds(10),
                Interleave = interleave,
            });
        return Task.CompletedTask;
    }

    public Task<int> GetTimerFireCountAsync() => Task.FromResult(_memory.Value.TimerFireCount);

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }

    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;
}
