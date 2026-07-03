using System.Buffers;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;

namespace Quark.Runtime.Clustering;

/// <summary>
///     <see cref="IGrainCallInvoker" /> that routes a grain call to a peer silo over a pooled,
///     multiplexed TCP connection. Silo-to-silo analogue of <c>TcpGatewayCallInvoker</c>.
///     Stamps <c>x-quark-hop: "1"</c> on every outbound request to prevent infinite forwarding.
/// </summary>
public sealed class SiloCallInvoker : IGrainCallInvoker
{
    private readonly SiloAddress _peer;
    private readonly SiloPeerConnection _connection;
    private readonly GrainMessageSerializer _grainSerializer;
    private readonly MessageSerializer _messageSerializer;
    private long _nextCorrelationId;

    // Test-only send delegate — null in production.
    private readonly Func<MessageEnvelope, CancellationToken, Task<MessageEnvelope>>? _testSendFn;

    public SiloCallInvoker(SiloAddress peer, SiloPeerConnection connection,
        GrainMessageSerializer grainSerializer, MessageSerializer messageSerializer)
    {
        _peer = peer;
        _connection = connection;
        _grainSerializer = grainSerializer;
        _messageSerializer = messageSerializer;
    }

    internal SiloCallInvoker(SiloAddress peer,
        Func<MessageEnvelope, CancellationToken, Task<MessageEnvelope>> testSendFn,
        GrainMessageSerializer grainSerializer, MessageSerializer messageSerializer)
    {
        _peer = peer;
        _connection = null!;
        _grainSerializer = grainSerializer;
        _messageSerializer = messageSerializer;
        _testSendFn = testSendFn;
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
            throw new InvalidOperationException(result.Error ?? $"Remote silo '{_peer}' grain invocation failed.");

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
            throw new InvalidOperationException(result.Error ?? $"Remote silo '{_peer}' grain invocation failed.");
    }

    public ValueTask InvokeObserverAsync<TInvokable>(
        GrainId grainId,
        TInvokable invokable,
        CancellationToken cancellationToken = default)
        where TInvokable : struct, IObserverVoidInvokable
        => throw new NotSupportedException(
            "Cross-silo observer push is not supported. Observer delivery is local only.");

    private Task<MessageEnvelope> SendAsync(
        GrainId grainId, uint methodId, byte[] argBytes, CancellationToken ct)
    {
        byte[] requestPayload = _grainSerializer.SerializeRequest(
            new GrainInvocationRequest(grainId, methodId, argBytes));

        long id = Interlocked.Increment(ref _nextCorrelationId);

        var headers = new MessageHeaders();
        headers.Set("x-quark-hop", "1");

        var envelope = new MessageEnvelope
        {
            CorrelationId = id,
            MessageType = MessageType.Request,
            Payload = requestPayload,
            Headers = headers
        };

        return _testSendFn is not null
            ? _testSendFn(envelope, ct)
            : _connection.SendAndAwaitAsync(envelope, ct);
    }
}
