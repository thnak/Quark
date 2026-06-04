using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Reminders;
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
    private readonly IGrainCallInvoker _invoker;
    private readonly GrainMessageSerializer _serializer;

    /// <summary>Initializes the dispatcher.</summary>
    public MessageDispatcher(
        TransportGrainDispatcherRegistry dispatcherRegistry,
        IGrainCallInvoker invoker,
        GrainMessageSerializer serializer)
    {
        _dispatcherRegistry = dispatcherRegistry;
        _invoker = invoker;
        _serializer = serializer;
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

        try
        {
            object? result;

            // Well-known reminder delivery uses a typed invokable directly — no registered
            // transport dispatcher needed per grain type.
            if (request.MethodId == ReminderMethodIds.ReceiveReminder)
            {
                var invokable = new ReceiveReminderInvokable(
                    (string)request.Arguments![0]!,
                    (TickStatus)request.Arguments[1]!);
                await _invoker.InvokeVoidAsync(request.GrainId, invokable, cancellationToken)
                    .ConfigureAwait(false);
                result = null;
            }
            else
            {
                ITransportGrainDispatcher dispatcher =
                    _dispatcherRegistry.GetDispatcher(request.GrainId.Type);
                result = await dispatcher
                    .DispatchAsync(request.GrainId, request.MethodId, request.Arguments, _invoker,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!expectResponse)
                return null;

            GrainInvocationResponse response = new(true, result, null);
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
            GrainInvocationResponse response = new(false, null, ex.ToString());
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
