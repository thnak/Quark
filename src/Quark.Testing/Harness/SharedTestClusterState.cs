using Quark.Runtime;
using Quark.Runtime.Clustering;

namespace Quark.Testing.Harness;

/// <summary>
///     Holds the shared grain directory, router, and membership table for a multi-silo test cluster.
///     One instance is created per <see cref="TestCluster" /> when <c>EnableClustering = true</c>.
/// </summary>
internal sealed class SharedTestClusterState
{
    public InMemoryGrainDirectory Directory { get; } = new();
    public InProcessSiloRouter Router { get; } = new();
    public InMemoryMembershipTable MembershipTable { get; } = new();
}
