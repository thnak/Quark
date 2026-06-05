using System.Collections.Concurrent;

namespace Quark.Runtime.Clustering;

/// <summary>
///     Process-scoped registry of shared state for localhost (in-process multi-silo) clusters.
///     Keyed by <c>"clusterId:serviceId"</c>; each unique cluster gets its own isolated state.
/// </summary>
internal static class SharedLocalhostCluster
{
    private static readonly ConcurrentDictionary<string, LocalhostClusterState> _clusters = new();

    /// <summary>Gets or creates the shared state for the given cluster key.</summary>
    internal static LocalhostClusterState GetOrCreate(string clusterKey)
        => _clusters.GetOrAdd(clusterKey, _ => new LocalhostClusterState());

    /// <summary>Removes the shared state for the given cluster key.</summary>
    internal static void Remove(string clusterKey)
        => _clusters.TryRemove(clusterKey, out _);
}

/// <summary>
///     Shared state for a single in-process localhost cluster (grain directory + router + membership).
/// </summary>
internal sealed class LocalhostClusterState
{
    public InMemoryGrainDirectory Directory { get; } = new();
    public InProcessSiloRouter Router { get; } = new();
    public InMemoryMembershipTable MembershipTable { get; } = new();
}
