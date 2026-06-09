using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired at the start of every grain method invocation.</summary>
public readonly struct InvocationStartEvent(GrainId grainId, uint methodId, bool isObserver)
{
    public GrainId GrainId { get; } = grainId;
    public uint MethodId { get; } = methodId;
    public bool IsObserver { get; } = isObserver;
}