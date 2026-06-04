using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Maps <see cref="GrainType" /> to the generated
///     <see cref="ITransportGrainDispatcher" /> for that grain type.
///     Populated at silo startup via <see cref="SiloHostedService" />.
/// </summary>
public sealed class TransportGrainDispatcherRegistry
{
    private readonly ConcurrentDictionary<string, ITransportGrainDispatcher> _dispatchers = new();

    public ITransportGrainDispatcher GetDispatcher(GrainType grainType)
    {
        if (_dispatchers.TryGetValue(grainType.Value, out ITransportGrainDispatcher? dispatcher))
            return dispatcher;
        throw new InvalidOperationException(
            $"No ITransportGrainDispatcher registered for grain type '{grainType.Value}'. " +
            "Call AddGrainTransportDispatcher() during startup.");
    }

    public bool TryGetDispatcher(GrainType grainType, out ITransportGrainDispatcher dispatcher)
        => _dispatchers.TryGetValue(grainType.Value, out dispatcher!);

    public void Register(GrainType grainType, ITransportGrainDispatcher dispatcher)
        => _dispatchers[grainType.Value] = dispatcher;
}
