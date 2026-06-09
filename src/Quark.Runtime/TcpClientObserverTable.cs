using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Silo-side table that maps TCP-client-side observer <see cref="GrainId" />s to write-back
///     delegates.  When <see cref="LocalGrainCallInvoker" /> cannot find an observer in the local
///     <see cref="ObserverRegistry" /> it falls back to this table; the registered delegate
///     serialises the invocation and writes an <c>ObserverInvoke</c> frame back to the TCP
///     connection that owns the observer.
/// </summary>
public sealed class TcpClientObserverTable
{
    private readonly ConcurrentDictionary<GrainId, Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>>
        _entries = new();

    public void Register(
        GrainId grainId,
        Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task> writeBack)
        => _entries[grainId] = writeBack;

    public bool TryGet(
        GrainId grainId,
        out Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>? writeBack)
        => _entries.TryGetValue(grainId, out writeBack);

    public void Unregister(GrainId grainId) => _entries.TryRemove(grainId, out _);

    public void RemoveAll(IEnumerable<GrainId> grainIds)
    {
        foreach (GrainId id in grainIds)
            _entries.TryRemove(id, out _);
    }
}
