using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime.Clustering;

/// <summary>
///     <see cref="ISiloRouter" /> for networked (cross-process) clusters.
///     Values are <see cref="SiloCallInvoker" /> instances installed by
///     <see cref="PeerConnectionManager" />.
///     Structurally identical to <see cref="InProcessSiloRouter" />; the networked behaviour
///     is entirely in the invoker values.
/// </summary>
public sealed class NetworkedSiloRouter : ISiloRouter
{
    private readonly ConcurrentDictionary<SiloAddress, IGrainCallInvoker> _invokers = new();

    public void Register(SiloAddress address, IGrainCallInvoker invoker)
        => _invokers[address] = invoker;

    public void Unregister(SiloAddress address)
        => _invokers.TryRemove(address, out _);

    public bool TryGetInvoker(SiloAddress address, [NotNullWhen(true)] out IGrainCallInvoker? invoker)
        => _invokers.TryGetValue(address, out invoker);
}
