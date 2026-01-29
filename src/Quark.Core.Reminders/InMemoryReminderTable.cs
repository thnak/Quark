using System.Collections.Concurrent;
using Quark.Abstractions.Reminders;
using Quark.Networking.Abstractions;

namespace Quark.Core.Reminders;

/// <summary>
///     In-memory implementation of reminder table for testing and single-silo scenarios.
/// </summary>
public sealed class InMemoryReminderTable : IReminderTable
{
    private readonly ConcurrentDictionary<string, Reminder> _reminders = new();
    private readonly IConsistentHashRing? _hashRing;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryReminderTable"/> class.
    /// </summary>
    /// <param name="hashRing">Optional consistent hash ring for distributed scenarios.</param>
    public InMemoryReminderTable(IConsistentHashRing? hashRing = null)
    {
        _hashRing = hashRing;
    }

    /// <inheritdoc />
    public Task RegisterAsync(Reminder reminder, CancellationToken cancellationToken = default)
    {
        var key = reminder.GetId();
        _reminders[key] = reminder;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterAsync(string actorId, string name, CancellationToken cancellationToken = default)
    {
        var key = $"{actorId}:{name}";
        _reminders.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Reminder>> GetRemindersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        var reminders = _reminders.Values
            .Where(r => r.ActorId == actorId)
            .ToList();
        return Task.FromResult<IReadOnlyList<Reminder>>(reminders);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Reminder>> GetDueRemindersForSiloAsync(
        string siloId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        var dueReminders = _reminders.Values
            .Where(r => r.NextFireTime <= utcNow)
            .Where(r => _hashRing == null || IsReminderOwnedBySilo(r, siloId))
            .ToList();
        
        return Task.FromResult<IReadOnlyList<Reminder>>(dueReminders);
    }

    /// <inheritdoc />
    public Task UpdateFireTimeAsync(
        string actorId,
        string name,
        DateTimeOffset lastFiredAt,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default)
    {
        var key = $"{actorId}:{name}";
        if (_reminders.TryGetValue(key, out var reminder))
        {
            reminder.LastFiredAt = lastFiredAt;
            reminder.NextFireTime = nextFireTime;
        }
        return Task.CompletedTask;
    }

    private bool IsReminderOwnedBySilo(Reminder reminder, string siloId)
    {
        if (_hashRing == null)
            return true;

        var ownerSilo = _hashRing.GetNode($"{reminder.ActorType}:{reminder.ActorId}");
        return ownerSilo == siloId;
    }
}
