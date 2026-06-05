using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Clustering;

/// <summary>
///     Represents a single silo's record in the cluster membership table.
/// </summary>
public sealed class MembershipEntry
{
    /// <summary>Network address of the silo.</summary>
    public required SiloAddress SiloAddress { get; init; }

    /// <summary>Human-readable name of the silo.</summary>
    public required string SiloName { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public SiloStatus Status { get; set; }

    /// <summary>UTC timestamp of the last successful IAmAlive heartbeat.</summary>
    public DateTime IAmAlive { get; set; }
}
