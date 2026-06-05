using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>Append-only log storage for journaled grains.</summary>
public interface ILogStorage
{
    Task<IReadOnlyList<LogEntry>> ReadEntriesAsync(GrainId grainId, int fromVersion, int toVersion, CancellationToken ct = default);
    Task AppendEntriesAsync(GrainId grainId, int expectedVersion, IReadOnlyList<LogEntry> entries, CancellationToken ct = default);
}
