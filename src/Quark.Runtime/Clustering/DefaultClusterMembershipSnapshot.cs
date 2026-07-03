using Quark.Core.Abstractions.Clustering;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Oracle-pushed snapshot of currently Active silos. Updated by
///     <see cref="PeerConnectionManager" /> each time membership changes.
/// </summary>
public sealed class DefaultClusterMembershipSnapshot : IClusterMembershipSnapshot
{
    private volatile IReadOnlyList<SiloAddress> _activeSilos;

    public DefaultClusterMembershipSnapshot(SiloAddress self)
    {
        _activeSilos = [self];
    }

    public IReadOnlyList<SiloAddress> ActiveSilos => _activeSilos;

    internal void Update(IReadOnlyList<SiloAddress> activeSilos)
    {
        _activeSilos = activeSilos;
    }
}
