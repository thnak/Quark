using Quark.Core.Abstractions.Identity;

namespace Quark.Reminders.Abstractions;

/// <summary>
///     Provider-level abstraction for persisting reminder entries.
///     Separate from <c>IGrainStorage</c> — reminders require cross-grain queries.
/// </summary>
public interface IReminderStorage
{
    Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default);
    Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default);
    Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default);
}
