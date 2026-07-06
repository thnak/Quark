using System.Collections.Concurrent;

namespace Quark.Streaming.InMemory;

public sealed class ImplicitStreamSubscriptionRegistry
{
    private sealed class NamespaceEntry
    {
        public readonly List<string> List = [];
        // volatile: write path holds lock on List; read path reads without lock.
        // Volatile ensures the new array reference is visible after the lock is released.
        public volatile string[] Snapshot = [];
    }

    private readonly ConcurrentDictionary<string, NamespaceEntry> _map = new(StringComparer.Ordinal);

    public void Register(string streamNamespace, string grainTypeKey)
    {
        var entry = _map.GetOrAdd(streamNamespace, _ => new NamespaceEntry());
        lock (entry.List)
        {
            entry.List.Add(grainTypeKey);
            entry.Snapshot = entry.List.ToArray();
        }
    }

    public bool TryGetGrainTypes(string streamNamespace, out IReadOnlyList<string> grainTypeKeys)
    {
        if (_map.TryGetValue(streamNamespace, out NamespaceEntry? entry))
        {
            string[] snapshot = entry.Snapshot;
            if (snapshot.Length > 0)
            {
                grainTypeKeys = snapshot;
                return true;
            }
        }

        grainTypeKeys = [];
        return false;
    }
}
