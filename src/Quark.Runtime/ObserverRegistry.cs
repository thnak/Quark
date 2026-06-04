using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Maps synthetic <see cref="GrainId" /> values assigned to local observer objects
///     to the target CLR object and its method invoker.
///     Used by <see cref="LocalGrainCallInvoker" /> to short-circuit calls to in-process observers
///     without going through <see cref="GrainActivationTable" />.
/// </summary>
public sealed class ObserverRegistry
{
    private readonly ConcurrentDictionary<GrainId, ObserverEntry> _entries = new();

    /// <summary>Registers a local observer object under a synthetic grain identity (typed-dispatch path).</summary>
    public void Register(GrainId grainId, object target)
        => _entries[grainId] = new ObserverEntry(target, null);

    /// <summary>Registers a local observer object under a synthetic grain identity (legacy object-array path).</summary>
    public void Register(GrainId grainId, object target, IObserverMethodInvoker invoker)
        => _entries[grainId] = new ObserverEntry(target, invoker);

    /// <summary>Attempts to find the observer entry for the given grain identity.</summary>
    public bool TryGet(GrainId grainId, out ObserverEntry entry)
        => _entries.TryGetValue(grainId, out entry!);

    /// <summary>Removes the observer registration for the given grain identity.</summary>
    public void Unregister(GrainId grainId)
        => _entries.TryRemove(grainId, out _);

    /// <summary>
    ///     Removes all observer registrations whose target is the same object as
    ///     <paramref name="target" /> (reference equality).  Used by
    ///     <c>DeleteObjectReference</c> when only the proxy (not the GrainId) is known.
    /// </summary>
    public void UnregisterByTarget(object target)
    {
        foreach (GrainId key in _entries.Keys)
        {
            if (_entries.TryGetValue(key, out ObserverEntry? entry) && ReferenceEquals(entry.Target, target))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Holds a local observer target and its optional legacy method invoker.</summary>
    public sealed record ObserverEntry(object Target, IObserverMethodInvoker? Invoker);
}
