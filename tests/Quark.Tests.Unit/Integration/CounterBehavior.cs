using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterBehavior : IGrainBehavior, ICounterGrain, IActivationLifecycle
{
    private readonly IActivationMemory<CounterState> _memory;
    private readonly IActivationShellAccessor _shell;

    public CounterBehavior(IActivationMemory<CounterState> memory, IActivationShellAccessor shell)
    {
        _memory = memory;
        _shell = shell;
    }

    private CounterState S => _memory.Value;

    public Task<long> IncrementAsync() { S.Value++; return Task.FromResult(S.Value); }
    public Task<long> GetValueAsync() => Task.FromResult(S.Value);
    public Task ResetAsync() { S.Value = 0; return Task.CompletedTask; }

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }

    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        S.DeactivateCalled = true;
        return Task.CompletedTask;
    }
}
