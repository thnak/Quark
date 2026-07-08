using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.AstroSim;

public sealed class ChunkGrainProxy : IChunkGrain, IGrainProxyActivator<ChunkGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public ChunkGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static ChunkGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask TickAsync()
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_TickInvokable());

    public ValueTask<ChunkAggregate> GetAggregateAsync()
        => _invoker.InvokeAsync<ChunkBehavior_GetAggregateInvokable, ChunkAggregate>(
            _grainId, new ChunkBehavior_GetAggregateInvokable());

    public ValueTask TransferBodyAsync(Body body)
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_TransferBodyInvokable(body));

    public ValueTask SeedAsync(IReadOnlyList<Body> bodies)
        => _invoker.InvokeVoidAsync(_grainId, new ChunkBehavior_SeedInvokable(bodies));
}
