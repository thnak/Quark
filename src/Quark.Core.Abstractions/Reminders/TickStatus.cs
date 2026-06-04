namespace Quark.Core.Abstractions.Reminders;

/// <summary>Status snapshot passed to <see cref="IRemindable.ReceiveReminder" />.</summary>
public readonly record struct TickStatus(
    DateTimeOffset FirstTickTime,
    TimeSpan Period,
    DateTimeOffset CurrentTickTime);
