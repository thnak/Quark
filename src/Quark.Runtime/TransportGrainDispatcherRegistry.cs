using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Maps <see cref="GrainType" /> to the generated
///     <see cref="ITransportGrainDispatcher" /> for that grain type.
///     Populated at silo startup via <see cref="SiloHostedService" />.
/// </summary>
public sealed class TransportGrainDispatcherRegistry : TransportDispatcherRegistry
{
    /// <summary>
    ///     Returns the dispatcher for <paramref name="grainType" />, throwing if none is registered.
    /// </summary>
    public ITransportGrainDispatcher GetDispatcher(GrainType grainType)
    {
        if (TryGet(grainType, out ITransportGrainDispatcher? dispatcher) && dispatcher is not null)
            return dispatcher;
        throw new InvalidOperationException(
            $"No ITransportGrainDispatcher registered for grain type '{grainType.Value}'. " +
            "Call AddGrainTransportDispatcher() during startup.");
    }
}
