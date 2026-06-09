using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

public sealed class ManagedBufferBehavior : IGrainBehavior, IManagedBufferGrain, IActivationLifecycle
{
    private readonly IManagedActivationMemory<ManagedBuffer> _managed;
    private readonly IActivationShellAccessor _shell;

    public ManagedBufferBehavior(
        IManagedActivationMemory<ManagedBuffer> managed,
        IActivationShellAccessor shell)
    {
        _shell = shell;
        _managed = managed
            .Init(() =>
            {
                var buf = new ManagedBuffer();
                buf.InitCount++;
                return ValueTask.FromResult(buf);
            })
            .Destroy(b => { b.DestroyCount++; return ValueTask.CompletedTask; });
    }

    public async Task<long> GetInitCountAsync()
    {
        var buf = await _managed.GetAsync();
        return buf.InitCount;
    }

    public async Task<string> GetDataAsync()
    {
        var buf = await _managed.GetAsync();
        return buf.Data;
    }

    public async Task SetDataAsync(string value)
    {
        var buf = await _managed.GetAsync();
        buf.Data = value;
    }

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }

    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;
}