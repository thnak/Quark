namespace Quark.Networking.Abstractions;

/// <summary>
///     Represents a node in the consistent hash ring.
/// </summary>
public sealed class HashRingNode
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HashRingNode" /> class.
    /// </summary>
    public HashRingNode(string siloId, int virtualNodeCount = 150)
    {
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        VirtualNodeCount = virtualNodeCount;
    }

    /// <summary>
    ///     Gets the silo ID.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    ///     Gets the number of virtual nodes for this physical node.
    ///     More virtual nodes = better distribution.
    /// </summary>
    public int VirtualNodeCount { get; }
}