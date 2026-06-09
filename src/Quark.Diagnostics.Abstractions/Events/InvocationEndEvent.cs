using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired after a grain method invocation completes (successfully or with error).</summary>
public readonly struct InvocationEndEvent(GrainId grainId, uint methodId, bool isObserver, TimeSpan elapsed, Exception? exception)
{
    public GrainId GrainId { get; } = grainId;
    public uint MethodId { get; } = methodId;
    public bool IsObserver { get; } = isObserver;
    public TimeSpan Elapsed { get; } = elapsed;
    public Exception? Exception { get; } = exception;
    public bool IsSuccess => Exception is null;
}