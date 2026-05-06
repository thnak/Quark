using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Hosting;
using Quark.Transport.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Default dispatcher for request/one-way message envelopes.
/// </summary>
public sealed class MessageDispatcher : IMessageDispatcher
{
    private readonly IGrainCallInvoker _invoker;
    private readonly GrainMessageSerializer _serializer;

    /// <summary>Initializes the dispatcher.</summary>
    public MessageDispatcher(IGrainCallInvoker invoker, GrainMessageSerializer serializer)
    {
        _invoker = invoker;
        _serializer = serializer;
    }

    /// <inheritdoc/>
    public async Task<MessageEnvelope?> DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.MessageType)
        {
            case MessageType.Request:
                return await DispatchRequestAsync(envelope, expectResponse: true, cancellationToken).ConfigureAwait(false);
            case MessageType.OneWayRequest:
                _ = await DispatchRequestAsync(envelope, expectResponse: false, cancellationToken).ConfigureAwait(false);
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
            if (!expectResponse)
            {
                await _invoker.InvokeVoidAsync(request.GrainId, request.MethodId, request.Arguments, cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            object? result = await _invoker.InvokeAsync(request.GrainId, request.MethodId, request.Arguments, cancellationToken)
                .ConfigureAwait(false);

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