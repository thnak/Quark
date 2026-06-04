using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Reminders.Abstractions;

namespace Quark.Reminders.InMemory;

/// <summary>
///     In-memory <see cref="IReminderStorage" /> for development and tests.
///     State is NOT durable across process restarts. Swap for Redis in production.
/// </summary>
public sealed class InMemoryReminderStorage : IReminderStorage
{
    private readonly ConcurrentDictionary<(GrainId, string), ReminderEntry> _store = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ReminderEntry>>(_store.Values.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = _store
            .Where(kv => kv.Key.Item1 == grainId)
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReminderEntry>>(result);
    }

    /// <inheritdoc />
    public Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store[(entry.GrainId, entry.ReminderName)] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _store.TryRemove((grainId, reminderName), out _);
        return Task.CompletedTask;
    }
}
