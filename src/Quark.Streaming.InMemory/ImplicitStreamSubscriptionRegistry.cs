using System.Collections.Concurrent;

namespace Quark.Streaming.InMemory;

public sealed class ImplicitStreamSubscriptionRegistry
{
    private readonly ConcurrentDictionary<string, List<string>> _map = new(StringComparer.Ordinal);

    public void Register(string streamNamespace, string grainTypeKey)
    {
        var list = _map.GetOrAdd(streamNamespace, _ => []);
        lock (list) list.Add(grainTypeKey);
    }

    public bool TryGetGrainTypes(string streamNamespace, out IReadOnlyList<string> grainTypeKeys)
    {
        if (_map.TryGetValue(streamNamespace, out var list))
        {
            lock (list) grainTypeKeys = [..list];
            return grainTypeKeys.Count > 0;
        }

        grainTypeKeys = [];
        return false;
    }
}
