using Quark.Serialization.Abstractions.Abstractions;
using Quark.Streaming.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="IAsyncStream{T}" /> implementation for the TCP gateway client.
///     Sends <see cref="MessageType.StreamSubscribe" /> requests and awaits ack responses.
/// </summary>
public sealed class TcpClientStream<T> : IAsyncStream<T>
{
    private readonly StreamId _streamId;
    private readonly TcpGatewayConnection _connection;
    private readonly TcpStreamPushDispatcher _dispatcher;
    private readonly IFieldCodec<T> _codec;
    private long _nextCorrelationId;

    public StreamId StreamId => _streamId;

    internal TcpClientStream(
        StreamId streamId,
        TcpGatewayConnection connection,
        TcpStreamPushDispatcher dispatcher,
        IFieldCodec<T> codec)
    {
        _streamId = streamId;
        _connection = connection;
        _dispatcher = dispatcher;
        _codec = codec;
    }

    public async ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var subId = Guid.NewGuid();

        var headers = new MessageHeaders();
        headers.Set("stream-ns", _streamId.Namespace);
        headers.Set("stream-key", _streamId.Key);
        headers.Set("sub-id", subId.ToString("N"));

        long correlationId = Interlocked.Increment(ref _nextCorrelationId);
        var envelope = new MessageEnvelope
        {
            CorrelationId = correlationId,
            MessageType = MessageType.StreamSubscribe,
            Headers = headers,
            Payload = ReadOnlyMemory<byte>.Empty
        };

        // Send and await ack response from the server.
        await _connection.SendAndAwaitAsync(envelope).ConfigureAwait(false);

        var sub = new TcpClientStreamSubscription<T>(subId, _streamId, observer, _codec);
        _dispatcher.Register(subId, sub);

        return new TcpStreamSubscriptionHandle<T>(subId, _streamId, _dispatcher, _connection);
    }

    public ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null)
        => SubscribeAsync(new DelegateObserver<T>(onNext, onError, onCompleted));

    public ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync<TContext>(TContext context,
        Func<TContext, T, StreamSequenceToken?, ValueTask> onNext, Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null)
        => SubscribeAsync(new DelegateContextObserver<T, TContext>(context, onNext, onError, onCompleted));

    public ValueTask<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles()
    {
        var handles = _dispatcher.GetForStream(_streamId)
            .OfType<TcpClientStreamSubscription<T>>()
            .Select(StreamSubscriptionHandle<T> (s) => new TcpStreamSubscriptionHandle<T>(s.SubId, _streamId, _dispatcher, _connection))
            .ToList();
        return ValueTask.FromResult<IList<StreamSubscriptionHandle<T>>>(handles);
    }

    public ValueTask OnNextAsync(T item, StreamSequenceToken? token = null)
        => throw new NotSupportedException("Clients cannot publish to streams.");

    public ValueTask OnErrorAsync(Exception ex)
        => throw new NotSupportedException("Clients cannot publish to streams.");

    public ValueTask OnCompletedAsync()
        => throw new NotSupportedException("Clients cannot publish to streams.");
}
