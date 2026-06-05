using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;

namespace Quark.Persistence.InMemory;

/// <summary>
///     In-memory <see cref="ILogStorage" /> for development and tests.
///     Not durable across process restarts.
/// </summary>
public sealed class InMemoryLogStorage : ILogStorage
{
    private readonly ConcurrentDictionary<GrainId, List<LogEntry>> _logs = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<LogEntry>> ReadEntriesAsync(
        GrainId grainId, int fromVersion, int toVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_logs.TryGetValue(grainId, out List<LogEntry>? log))
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);

        List<LogEntry> slice;
        lock (log)
        {
            slice = log.Where(e => e.Version >= fromVersion && e.Version < toVersion).ToList();
        }
        return Task.FromResult<IReadOnlyList<LogEntry>>(slice);
    }

    /// <inheritdoc />
    public Task AppendEntriesAsync(
        GrainId grainId, int expectedVersion, IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<LogEntry> log = _logs.GetOrAdd(grainId, _ => []);
        lock (log)
        {
            if (log.Count != expectedVersion)
                throw new InvalidOperationException(
                    $"Version conflict: expected {expectedVersion}, found {log.Count}.");
            log.AddRange(entries);
        }
        return Task.CompletedTask;
    }
}
