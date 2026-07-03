using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Clustering;

/// <summary>Cheap, cached view of the currently Active silos for placement decisions.</summary>
public interface IClusterMembershipSnapshot
{
    /// <summary>Active silo addresses (includes self). Refreshed by the membership oracle.</summary>
    IReadOnlyList<SiloAddress> ActiveSilos { get; }
}
