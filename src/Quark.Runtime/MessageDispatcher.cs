using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Reminders;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Default dispatcher for request/one-way message envelopes.
///     Routes each incoming message to the correct grain via the
///     <see cref="TransportGrainDispatcherRegistry" />.
/// </summary>
public sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly TransportGrainDispatcherRegistry _dispatcherRegistry;
    private readonly IGrainFactory? _grainFactory;
    private readonly IGrainCallInvoker _invoker;
    private readonly IGrainCallInvoker? _terminalInvoker;
    private readonly GrainMessageSerializer _serializer;

    /// <summary>Initializes the dispatcher.</summary>
    public MessageDispatcher(
        TransportGrainDispatcherRegistry dispatcherRegistry,
        IGrainCallInvoker invoker,
        GrainMessageSerializer serializer,
        IGrainFactory? grainFactory = null,
        IGrainCallInvoker? terminalInvoker = null)
    {
        _dispatcherRegistry = dispatcherRegistry;
        _invoker = invoker;
        _serializer = serializer;
        _grainFactory = grainFactory;
        _terminalInvoker = terminalInvoker;
    }

    /// <inheritdoc />
    public async Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.MessageType)
        {
            case MessageType.Request:
                return await DispatchRequestAsync(envelope, true, cancellationToken).ConfigureAwait(false);
            case MessageType.OneWayRequest:
                _ = await DispatchRequestAsync(envelope, false, cancellationToken).ConfigureAwait(false);
                return null;
            default:
                return null;
        }
    }

    private async Task<MessageEnvelope?> DispatchRequestAsync(
        MessageEnvelope envelope,
        bool expectResponse,
        CancellationToken cancellationToken)
    {
        GrainInvocationRequest request = _serializer.DeserializeRequest(envelope.Payload);

        // Select local-terminal invoker for forwarded hops to prevent re-routing loops.
        // x-quark-hop: 1 is stamped by SiloCallInvoker on every silo-to-silo forwarded request.
        IGrainCallInvoker activeInvoker =
            (envelope.Headers?.Get("x-quark-hop") is not null && _terminalInvoker is not null)
            ? _terminalInvoker
            : _invoker;

        try
        {
            ReadOnlyMemory<byte> resultPayload;

            // Well-known reminder delivery uses a typed invokable directly — no registered
            // transport dispatcher needed per grain type.
            if (request.MethodId == ReminderMethodIds.ReceiveReminder)
            {
                CodecReader reminderReader = new(request.ArgumentPayload);
                string reminderName = (string)GrainMessageSerializer.ReadArg(ref reminderReader)!;
                var tickStatus = (TickStatus)GrainMessageSerializer.ReadArg(ref reminderReader)!;
                var invokable = new ReceiveReminderInvokable(reminderName, tickStatus);
                await activeInvoker.InvokeVoidAsync(request.GrainId, invokable, cancellationToken)
                    .ConfigureAwait(false);
                resultPayload = ReadOnlyMemory<byte>.Empty;
            }
            else
            {
                ITransportGrainDispatcher dispatcher =
                    _dispatcherRegistry.GetDispatcher(request.GrainId.Type);
                resultPayload = await dispatcher
                    .DispatchAsync(request.GrainId, request.MethodId, request.ArgumentPayload,
                        activeInvoker, _grainFactory, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!expectResponse)
                return null;

            GrainInvocationResponse response = new(true, resultPayload, null);
            return new MessageEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                MessageType = MessageType.Response,
                Headers = envelope.Headers,
                Payload = _serializer.SerializeResponse(response)
            };
        }
        catch (Exception ex) when (expectResponse)
        {
            GrainInvocationResponse response = new(false, ReadOnlyMemory<byte>.Empty, ex.ToString());
            return new MessageEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                MessageType = MessageType.Response,
                Headers = envelope.Headers,
                Payload = _serializer.SerializeResponse(response)
            };
        }
    }
}
