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
    private readonly IRequestDedupStore? _dedupStore;

    /// <summary>Initializes the dispatcher.</summary>
    public MessageDispatcher(
        TransportGrainDispatcherRegistry dispatcherRegistry,
        IGrainCallInvoker invoker,
        GrainMessageSerializer serializer,
        IGrainFactory? grainFactory = null,
        IGrainCallInvoker? terminalInvoker = null,
        IRequestDedupStore? dedupStore = null)
    {
        _dispatcherRegistry = dispatcherRegistry;
        _invoker = invoker;
        _serializer = serializer;
        _grainFactory = grainFactory;
        _terminalInvoker = terminalInvoker;
        _dedupStore = dedupStore;
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
            (envelope.Headers?.Get(QuarkHeaders.Hop) is not null && _terminalInvoker is not null)
            ? _terminalInvoker
            : _invoker;

        // Propagate idempotency key to the ambient AsyncLocal so that ICallContext.IdempotencyKey
        // is set when GrainScopeBinder stamps it onto the per-call scope.
        string? idempotencyKey = envelope.Headers?.Get(QuarkHeaders.IdempotencyKey);
        using IDisposable? keyScope = idempotencyKey is not null
            ? QuarkRequestContext.WithIdempotencyKey(idempotencyKey)
            : null;

        // Dedup checkpoint: skip when there is no key, no store, or the call is transactional.
        bool isDedupCall = idempotencyKey is not null
            && _dedupStore is not null
            && envelope.Headers?.Get(QuarkHeaders.Transaction) is null;

        if (isDedupCall)
        {
            ulong argHash = ComputeFnv1AHash(request.ArgumentPayload);
            DedupLease lease = await _dedupStore!.TryBeginAsync(
                request.GrainId, idempotencyKey!, argHash, cancellationToken).ConfigureAwait(false);

            if (lease.Outcome == DedupOutcome.Replay)
            {
                if (!expectResponse) return null;
                return new MessageEnvelope
                {
                    CorrelationId = envelope.CorrelationId,
                    MessageType = MessageType.Response,
                    Headers = envelope.Headers,
                    Payload = lease.RecordedResponse
                };
            }

            if (lease.Outcome == DedupOutcome.Conflict)
            {
                if (!expectResponse) return null;
                return new MessageEnvelope
                {
                    CorrelationId = envelope.CorrelationId,
                    MessageType = MessageType.Response,
                    Headers = envelope.Headers,
                    Payload = _serializer.SerializeResponse(new GrainInvocationResponse(
                        false, ReadOnlyMemory<byte>.Empty,
                        "IdempotencyKeyConflict: the idempotency key was reused with different arguments."))
                };
            }
            // DedupOutcome.Execute: fall through to normal execution below.
        }

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

            if (isDedupCall)
            {
                // Cache the outcome (success or empty for one-way) so retries replay it.
                ReadOnlyMemory<byte> dedupPayload = expectResponse
                    ? _serializer.SerializeResponse(new GrainInvocationResponse(true, resultPayload, null))
                    : ReadOnlyMemory<byte>.Empty;
                _dedupStore!.Complete(request.GrainId, idempotencyKey!, dedupPayload);
                if (!expectResponse) return null;
                return new MessageEnvelope
                {
                    CorrelationId = envelope.CorrelationId,
                    MessageType = MessageType.Response,
                    Headers = envelope.Headers,
                    Payload = dedupPayload
                };
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
        catch (Exception ex) when (isDedupCall || expectResponse)
        {
            var errorResponse = new GrainInvocationResponse(false, ReadOnlyMemory<byte>.Empty, ex.ToString());
            byte[] errorBytes = _serializer.SerializeResponse(errorResponse);

            if (isDedupCall)
                _dedupStore!.Complete(request.GrainId, idempotencyKey!, errorBytes);

            // Re-throw for one-way keyed calls after recording the outcome.
            if (!expectResponse) throw;

            return new MessageEnvelope
            {
                CorrelationId = envelope.CorrelationId,
                MessageType = MessageType.Response,
                Headers = envelope.Headers,
                Payload = errorBytes
            };
        }
    }

    private static ulong ComputeFnv1AHash(ReadOnlyMemory<byte> payload)
    {
        const ulong fnvPrime = 0x100000001B3UL;
        const ulong offsetBasis = 0xCBF29CE484222325UL;
        ulong hash = offsetBasis;
        foreach (byte b in payload.Span)
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return hash;
    }
}
