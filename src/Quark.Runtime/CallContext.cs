using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal sealed class CallContext : ICallContext, ICallContextSetter
{
    public GrainId GrainId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void Set(GrainId grainId) => GrainId = grainId;
    public void SetIdempotencyKey(string? key) => IdempotencyKey = key;
}
