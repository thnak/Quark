using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a best-effort cascade to a child grain fails (remote send error or directory miss).</summary>
public readonly struct ChildTerminationFailedEvent(GrainId child, Exception? error)
{
    public GrainId Child { get; } = child;
    public Exception? Error { get; } = error;
}
