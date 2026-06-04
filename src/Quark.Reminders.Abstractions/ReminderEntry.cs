using Quark.Core.Abstractions.Identity;

namespace Quark.Reminders.Abstractions;

/// <summary>The durable record persisted by <see cref="IReminderStorage" /> for each reminder.</summary>
public sealed record ReminderEntry
{
    public required GrainId GrainId { get; init; }
    public required string ReminderName { get; init; }
    public required DateTimeOffset StartAt { get; init; }    // wall-clock when first registered
    public required TimeSpan Period { get; init; }
    public required DateTimeOffset NextFireAt { get; init; } // pre-computed; advanced after each tick
}
