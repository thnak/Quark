using System.Buffers;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IGrainCallInvoker" /> that routes grain calls over TCP to the silo gateway.
///     Observer invocations are not supported (local-only).
/// </summary>
public sealed class TcpGatewayCallInvoker : IGrainCallInvoker
{
    private readonly TcpGatewayConnection _connection;
    private readonly GrainMessageSerializer _grainSerializer;
    private long _nextCorrelationId;

    public TcpGatewayCallInvoker(TcpGatewayConnection connection, GrainMessageSerializer grainSerializer)
    {
        _connection = connection;
        _grainSerializer = grainSerializer;
    }

    public async Task<TResult> InvokeAsync<TInvokable, TResult>(
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
            throw new InvalidOperationException(result.Error ?? "Remote grain invocation failed.");
        return (TResult)result.Result!;
    }

    public async Task InvokeVoidAsync<TInvokable>(
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
            throw new InvalidOperationException(result.Error ?? "Remote grain invocation failed.");
    }

    public Task InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable
        => throw new NotSupportedException(
            "Observer invocations are local-only and cannot travel over TCP.");

    private async Task<MessageEnvelope> SendAsync(
        GrainId grainId, uint methodId, byte[] argBytes, CancellationToken ct)
    {
        byte[] requestPayload = _grainSerializer.SerializeRequest(
            new GrainInvocationRequest(grainId, methodId, argBytes));
        long id = Interlocked.Increment(ref _nextCorrelationId);
        return await _connection.SendAndAwaitAsync(new MessageEnvelope
        {
            CorrelationId = id,
            MessageType = MessageType.Request,
            Payload = requestPayload
        }, ct).ConfigureAwait(false);
    }
}
