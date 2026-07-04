using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

public sealed class EagerResourceBehavior : IEagerResourceGrain, IActivationLifecycle
{
    private readonly IEagerActivationMemory<EagerResource> _eager;
    private readonly IActivationShellAccessor _shell;

    public EagerResourceBehavior(
        IEagerActivationMemory<EagerResource> eager,
        IActivationShellAccessor shell)
    {
        _shell = shell;
        _eager = eager
            .Load((sp, _) =>
            {
                var svc = sp.GetRequiredService<EagerScopedService>();
                return ValueTask.FromResult(new EagerResource
                {
                    LoadedById = svc.Id,
                    InitCount = 1,
                });
            })
            .Destroy(r => { r.DestroyCount++; return ValueTask.CompletedTask; });
    }

    public Task OnActivateAsync(CancellationToken ct)
    {
        _eager.Value.ValueAvailableInOnActivate = true;
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public Task<string> GetLoadedByIdAsync() => Task.FromResult(_eager.Value.LoadedById);
    public Task<int> GetInitCountAsync() => Task.FromResult(_eager.Value.InitCount);
    public Task<bool> WasValueAvailableInOnActivateAsync() => Task.FromResult(_eager.Value.ValueAvailableInOnActivate);

    public Task SelfDestructAsync()
    {
        _shell.Shell.Deactivate(DeactivationReason.ApplicationRequested);
        return Task.CompletedTask;
    }
}
