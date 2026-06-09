using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     Client-side dispatcher for incoming <see cref="MessageType.ObserverInvoke" /> frames.
///     Looks up the local observer target in <see cref="ObserverRegistry" /> and routes the
///     call through the registered <see cref="ITransportGrainDispatcher" /> for that observer type.
/// </summary>
public sealed class TcpObserverDispatcher
{
    private readonly ObserverTransportDispatcherRegistry _dispatcherRegistry;
    private readonly LocalObserverCallInvoker _invoker;

    public TcpObserverDispatcher(
        ObserverRegistry observerRegistry,
        ObserverTransportDispatcherRegistry dispatcherRegistry)
    {
        _dispatcherRegistry = dispatcherRegistry;
        _invoker = new LocalObserverCallInvoker(observerRegistry);
    }

    public async Task DispatchAsync(MessageEnvelope envelope)
    {
        string? grainTypeName = envelope.Headers?.Get("grain-type");
        string? grainKey = envelope.Headers?.Get("grain-key");
        string? methodIdStr = envelope.Headers?.Get("method-id");

        if (grainTypeName is null || grainKey is null || methodIdStr is null)
            return;
        if (!uint.TryParse(methodIdStr, out uint methodId))
            return;

        if (!_dispatcherRegistry.TryGet(new GrainType(grainTypeName), out ITransportGrainDispatcher? dispatcher)
            || dispatcher is null)
            return;

        GrainId grainId = GrainId.Create(new GrainType(grainTypeName), grainKey);
        await dispatcher.DispatchAsync(grainId, methodId, envelope.Payload, _invoker, null)
            .ConfigureAwait(false);
    }
}
