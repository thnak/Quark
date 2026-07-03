using System.Buffers;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IGrainCallInvoker" /> that routes grain calls over TCP to the silo gateway.
///     Observer calls on locally-registered observers are dispatched in-process via
///     <see cref="ObserverRegistry" />.
/// </summary>
public sealed class TcpGatewayCallInvoker : IGrainCallInvoker
{
    private readonly TcpGatewayConnection _connection;
    private readonly GrainMessageSerializer _grainSerializer;
    private readonly ObserverRegistry? _observerRegistry;
    private long _nextCorrelationId;

    public TcpGatewayCallInvoker(TcpGatewayConnection connection, GrainMessageSerializer grainSerializer,
        ObserverRegistry? observerRegistry = null)
    {
        _connection = connection;
        _grainSerializer = grainSerializer;
        _observerRegistry = observerRegistry;
    }

    public async ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainInvokable<TResult>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        invokable.Serialize(ref writer);

        MessageEnvelope response = await SendAsync(
            grainId, invokable.MethodId, buffer.WrittenMemory.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        GrainInvocationResponse result = _grainSerializer.DeserializeResponse(response.Payload);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Remote grain invocation failed.");
        }

        var resultReader = new CodecReader(result.ResultPayload);
        return invokable.DeserializeResult(ref resultReader);
    }

    public async ValueTask InvokeVoidAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IGrainVoidInvokable
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        invokable.Serialize(ref writer);

        MessageEnvelope response = await SendAsync(
            grainId, invokable.MethodId, buffer.WrittenMemory.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        GrainInvocationResponse result = _grainSerializer.DeserializeResponse(response.Payload);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Remote grain invocation failed.");
        }
    }

    public ValueTask InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable
    {
        if (_observerRegistry?.TryGet(grainId, out ObserverRegistry.ObserverEntry entry) == true)
        {
            return invokable.Invoke(entry.Target);
        }

        throw new NotSupportedException(
            $"Observer '{grainId}' is not registered locally. " +
            "Call CreateObjectReference before invoking observer methods.");
    }

    private async Task<MessageEnvelope> SendAsync(
        GrainId grainId, uint methodId, byte[] argBytes, CancellationToken ct)
    {
        byte[] requestPayload = _grainSerializer.SerializeRequest(
            new GrainInvocationRequest(grainId, methodId, argBytes));
        long id = Interlocked.Increment(ref _nextCorrelationId);

        string? idempotencyKey = QuarkRequestContext.IdempotencyKey;
        MessageHeaders? headers = null;
        if (idempotencyKey is not null)
        {
            headers = new MessageHeaders();
            headers.Set(QuarkHeaders.IdempotencyKey, idempotencyKey);
        }

        return await _connection.SendAndAwaitAsync(new MessageEnvelope
        {
            CorrelationId = id,
            MessageType = MessageType.Request,
            Payload = requestPayload,
            Headers = headers
        }, ct).ConfigureAwait(false);
    }
}
