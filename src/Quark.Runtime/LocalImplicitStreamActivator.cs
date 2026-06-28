using Quark.Streaming.Abstractions;

namespace Quark.Runtime;

internal sealed class LocalImplicitStreamActivator : IImplicitStreamActivator
{
    private readonly LocalGrainCallInvoker _invoker;

    public LocalImplicitStreamActivator(LocalGrainCallInvoker invoker) => _invoker = invoker;

    public Task EnsureActivatedAsync(string grainTypeKey, string streamKey, CancellationToken cancellationToken = default)
        => _invoker.EnsureActivatedAsync(GrainId.Create(new GrainType(grainTypeKey), streamKey), cancellationToken);
}
