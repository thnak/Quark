using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Clustering;

/// <summary>
///     Persistent store for cluster silo membership.
///     Each silo writes its own row; the membership oracle monitors all rows.
/// </summary>
public interface IMembershipTable
{
    /// <summary>Returns all current membership entries.</summary>
    Task<IReadOnlyList<MembershipEntry>> ReadAllAsync(CancellationToken ct = default);

    /// <summary>Inserts a new row. Throws if the address already exists.</summary>
    Task InsertRowAsync(MembershipEntry entry, CancellationToken ct = default);

    /// <summary>Updates the status and IAmAlive of an existing row.</summary>
    Task UpdateRowAsync(MembershipEntry entry, CancellationToken ct = default);

    /// <summary>Updates only the IAmAlive timestamp for the given silo.</summary>
    Task UpdateIAmAliveAsync(SiloAddress address, DateTime iAmAlive, CancellationToken ct = default);
}
