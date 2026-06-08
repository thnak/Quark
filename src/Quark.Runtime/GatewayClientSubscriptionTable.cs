using System.Collections.Concurrent;

namespace Quark.Runtime;

/// <summary>
///     Tracks active <see cref="GatewayClientSubscription" /> instances per silo
///     so they can be cleaned up when a client connection closes.
/// </summary>
public sealed class GatewayClientSubscriptionTable
{
    private readonly ConcurrentDictionary<Guid, GatewayClientSubscription> _all = new();

    public void Add(GatewayClientSubscription sub) => _all[sub.SubId] = sub;

    public void Remove(Guid subId) => _all.TryRemove(subId, out _);

    public void RemoveAll(IEnumerable<Guid> subIds)
    {
        foreach (var id in subIds)
            _all.TryRemove(id, out _);
    }
}
