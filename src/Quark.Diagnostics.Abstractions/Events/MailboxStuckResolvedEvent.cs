using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Fired by <see cref="StuckGrainDetector" /> when a previously-stuck grain becomes idle again
///     (its work item completed).
/// </summary>
public readonly struct MailboxStuckResolvedEvent(GrainId grainId, TimeSpan totalStuckDuration)
{
    public GrainId GrainId { get; } = grainId;
    public TimeSpan TotalStuckDuration { get; } = totalStuckDuration;
}