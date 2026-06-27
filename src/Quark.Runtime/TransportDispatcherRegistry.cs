using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Shared base for registries that map a <see cref="GrainType" /> to its generated
///     <see cref="ITransportGrainDispatcher" />. Thread-safe; keyed on <see cref="GrainType.Value" />.
///     Concrete registries (<see cref="TransportGrainDispatcherRegistry" /> on the silo,
///     <c>ObserverTransportDispatcherRegistry</c> on the client) add only their own conveniences.
/// </summary>
public abstract class TransportDispatcherRegistry
{
    private readonly ConcurrentDictionary<string, ITransportGrainDispatcher> _dispatchers = new();

    /// <summary>Registers (or replaces) the dispatcher for <paramref name="grainType" />.</summary>
    public void Register(GrainType grainType, ITransportGrainDispatcher dispatcher)
        => _dispatchers[grainType.Value] = dispatcher;

    /// <summary>Attempts to look up the dispatcher for <paramref name="grainType" />.</summary>
    public bool TryGet(GrainType grainType, out ITransportGrainDispatcher? dispatcher)
        => _dispatchers.TryGetValue(grainType.Value, out dispatcher);
}
