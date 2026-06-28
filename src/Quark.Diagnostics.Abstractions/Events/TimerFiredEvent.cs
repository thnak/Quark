using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired on the silo after a grain timer callback completes (successfully or with error).</summary>
public readonly struct TimerFiredEvent(GrainId grainId, TimeSpan elapsed, Exception? exception)
{
    public GrainId GrainId { get; } = grainId;
    public TimeSpan Elapsed { get; } = elapsed;
    public Exception? Exception { get; } = exception;
    public bool IsSuccess => Exception is null;
}
