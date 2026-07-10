using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.ActorLifecycle;

public sealed class ActorLifecycleGrainBehavior : IGrainBehavior, IActorLifecycleGrain, IActivationLifecycle
{
    public ValueTask PingAsync() => default;

    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;
}
