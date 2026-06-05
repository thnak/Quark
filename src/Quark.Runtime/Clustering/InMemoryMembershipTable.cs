using System.Collections.Concurrent;
using Quark.Core.Abstractions.Clustering;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Thread-safe in-memory implementation of <see cref="IMembershipTable" />.
///     Suitable for localhost clustering and unit testing; not durable across restarts.
/// </summary>
public sealed class InMemoryMembershipTable : IMembershipTable
{
    private readonly ConcurrentDictionary<SiloAddress, MembershipEntry> _rows = new();

    /// <inheritdoc />
    public Task<IReadOnlyList<MembershipEntry>> ReadAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MembershipEntry>>(_rows.Values.ToList());
    }

    /// <inheritdoc />
    public Task InsertRowAsync(MembershipEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_rows.TryAdd(entry.SiloAddress, entry))
            throw new InvalidOperationException(
                $"Membership entry for {entry.SiloAddress} already exists.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateRowAsync(MembershipEntry entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _rows[entry.SiloAddress] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateIAmAliveAsync(SiloAddress address, DateTime iAmAlive, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_rows.TryGetValue(address, out MembershipEntry? entry))
            entry.IAmAlive = iAmAlive;
        return Task.CompletedTask;
    }
}
