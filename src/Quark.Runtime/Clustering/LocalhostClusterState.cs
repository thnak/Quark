namespace Quark.Runtime.Clustering;

/// <summary>
///     Shared state for a single in-process localhost cluster (grain directory + router + membership).
/// </summary>
internal sealed class LocalhostClusterState
{
    public InMemoryGrainDirectory Directory { get; } = new();
    public InProcessSiloRouter Router { get; } = new();
    public InMemoryMembershipTable MembershipTable { get; } = new();
}