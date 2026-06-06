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
    private readonly GrainMessageSerializer _serializer;

    public MessageDispatcher(
        TransportGrainDispatcherRegistry dispatcherRegistry,
        IGrainCallInvoker invoker,
        GrainMessageSerializer serializer,
        IGrainFactory? grainFactory = null)
    {
        _dispatcherRegistry = dispatcherRegistry;
        _invoker = invoker;
        _serializer = serializer;
        _grainFactory = grainFactory;
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

            if (request.MethodId == ReminderMethodIds.ReceiveReminder)
            {
                CodecReader reminderReader = new(request.ArgumentPayload);
                string reminderName = (string)GrainMessageSerializer.ReadArg(ref reminderReader)!;
                var tickStatus = (TickStatus)GrainMessageSerializer.ReadArg(ref reminderReader)!;
                var invokable = new ReceiveReminderInvokable(reminderName, tickStatus);
                await _invoker.InvokeVoidAsync(request.GrainId, invokable, cancellationToken)
                    .ConfigureAwait(false);
                result = null;
            }
            else
            {
                ITransportGrainDispatcher dispatcher =
                    _dispatcherRegistry.GetDispatcher(request.GrainId.Type);
                result = await dispatcher
                    .DispatchAsync(request.GrainId, request.MethodId, request.ArgumentPayload,
                        _invoker, _grainFactory, cancellationToken)
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
