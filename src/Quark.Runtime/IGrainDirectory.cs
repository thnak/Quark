using Quark.Core.Abstractions;
using System.Collections.Concurrent;

namespace Quark.Runtime;

/// <summary>
/// Stores the location of grain activations (which silo hosts each grain).
/// </summary>
public interface IGrainDirectory
{
    /// <summary>
    /// Registers the activation of <paramref name="grainId"/> on <paramref name="siloAddress"/>.
    /// Returns <c>false</c> if another silo already owns it (use <paramref name="existing"/>).
    /// </summary>
    bool TryRegister(GrainId grainId, SiloAddress siloAddress, out SiloAddress existing);

    /// <summary>
    /// Removes the registration for <paramref name="grainId"/> if it matches <paramref name="siloAddress"/>.
    /// </summary>
    bool TryUnregister(GrainId grainId, SiloAddress siloAddress);

    /// <summary>
    /// Looks up the current activation address for <paramref name="grainId"/>.
    /// </summary>
    bool TryLookup(GrainId grainId, out SiloAddress siloAddress);
}

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
