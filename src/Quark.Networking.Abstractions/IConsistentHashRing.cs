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

/// <summary>
///     Provides consistent hashing for actor placement in a distributed cluster.
///     Uses a hash ring with virtual nodes to ensure even distribution.
/// </summary>
public interface IConsistentHashRing
{
    /// <summary>
    ///     Gets the number of physical nodes in the ring.
    /// </summary>
    int NodeCount { get; }

    /// <summary>
    ///     Adds a node to the hash ring.
    /// </summary>
    /// <param name="node">The node to add.</param>
    void AddNode(HashRingNode node);

    /// <summary>
    ///     Removes a node from the hash ring.
    /// </summary>
    /// <param name="siloId">The silo ID to remove.</param>
    /// <returns>True if the node was removed, false if it wasn't found.</returns>
    bool RemoveNode(string siloId);

    /// <summary>
    ///     Gets the node responsible for a given key (actor ID).
    ///     Returns the first node clockwise from the key's hash position.
    /// </summary>
    /// <param name="key">The key (typically actor ID + actor type).</param>
    /// <returns>The silo ID responsible for this key, or null if no nodes exist.</returns>
    string? GetNode(string key);

    /// <summary>
    ///     Gets all nodes in the ring.
    /// </summary>
    /// <returns>A collection of all silo IDs in the ring.</returns>
    IReadOnlyCollection<string> GetAllNodes();
}