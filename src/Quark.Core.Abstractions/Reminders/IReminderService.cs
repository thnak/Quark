using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Reminders;

/// <summary>
///     Runtime service that manages durable reminder scheduling.
///     Exposed via <see cref="Hosting.IGrainContext.ReminderService" />.
/// </summary>
public interface IReminderService
{
    /// <summary>Registers or updates a durable reminder for a grain.</summary>
    /// <param name="grainId">The grain that owns the reminder.</param>
    /// <param name="name">Stable reminder name; re-registering with the same name replaces the existing entry.</param>
    /// <param name="dueTime">Delay before the first tick. Use <see cref="TimeSpan.Zero" /> to fire immediately.</param>
    /// <param name="period">
    ///     Interval between subsequent ticks. Must be at least as long as the polling interval
    ///     configured via <c>ReminderOptions.PollInterval</c> (default 1 s); shorter periods
    ///     will fire at the poll cadence, not the requested cadence.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        CancellationToken ct = default);

    /// <summary>Cancels a previously registered reminder. No-op if the reminder does not exist.</summary>
    /// <param name="grainId">The grain that owns the reminder.</param>
    /// <param name="name">Name of the reminder to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnregisterReminderAsync(
        GrainId grainId, string name,
        CancellationToken ct = default);

    /// <summary>Returns all reminders currently registered by the given grain.</summary>
    /// <param name="grainId">The grain whose reminders to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(
        GrainId grainId,
        CancellationToken ct = default);
}
