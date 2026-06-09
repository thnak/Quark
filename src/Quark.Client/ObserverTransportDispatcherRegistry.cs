using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
///     Maps observer <see cref="GrainType" />s to their generated
///     <see cref="ITransportGrainDispatcher" /> implementations.
///     Used by the TCP client to dispatch incoming
///     <see cref="Quark.Transport.Abstractions.MessageType.ObserverInvoke" /> frames to local
///     observer implementations.
/// </summary>
public sealed class ObserverTransportDispatcherRegistry
{
    private readonly Dictionary<string, ITransportGrainDispatcher> _dispatchers = new();

    public void Register(GrainType grainType, ITransportGrainDispatcher dispatcher)
        => _dispatchers[grainType.Value] = dispatcher;

    public bool TryGet(GrainType grainType, out ITransportGrainDispatcher? dispatcher)
        => _dispatchers.TryGetValue(grainType.Value, out dispatcher);
}
