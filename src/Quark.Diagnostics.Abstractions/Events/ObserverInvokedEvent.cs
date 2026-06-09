using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired on the silo after an observer method invocation is dispatched (success or error).</summary>
public readonly struct ObserverInvokedEvent(GrainId observerId, uint methodId, bool success, Exception? exception)
{
    public GrainId ObserverId { get; } = observerId;
    public uint MethodId { get; } = methodId;
    public bool Success { get; } = success;
    public Exception? Exception { get; } = exception;
}