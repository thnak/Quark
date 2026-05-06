using System.Collections.Concurrent;
using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
/// In-process in-memory implementation of <see cref="IGrainDirectory"/>.
/// Suitable for single-silo deployments and unit testing.
/// </summary>
public sealed class InMemoryGrainDirectory : IGrainDirectory
{
    private readonly ConcurrentDictionary<GrainId, SiloAddress> _store = new();

    /// <inheritdoc/>
    public bool TryRegister(GrainId grainId, SiloAddress siloAddress, out SiloAddress existing)
    {
        // AddOrUpdate semantics: register only if not present.
        SiloAddress registered = _store.GetOrAdd(grainId, siloAddress);
        if (registered == siloAddress)
        {
            existing = default;
            return true;
        }
        existing = registered;
        return false;
    }

    /// <inheritdoc/>
    public bool TryUnregister(GrainId grainId, SiloAddress siloAddress)
    {
        // Only remove if the stored address still matches (avoid removing a replacement).
        return ((ICollection<KeyValuePair<GrainId, SiloAddress>>)_store)
            .Remove(new KeyValuePair<GrainId, SiloAddress>(grainId, siloAddress));
    }

    /// <inheritdoc/>
    public bool TryLookup(GrainId grainId, out SiloAddress siloAddress) =>
        _store.TryGetValue(grainId, out siloAddress);

    /// <summary>Returns the current count of registered activations.</summary>
    public int Count => _store.Count;
}