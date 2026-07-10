using Quark.Streaming.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     <see cref="StreamSubscriptionHandle{T}" /> for a TCP gateway client subscription.
///     Wraps a <see cref="TcpClientStreamSubscription{T}" /> and sends
///     <see cref="MessageType.StreamUnsubscribe" /> one-way on unsubscribe.
/// </summary>
public sealed class TcpStreamSubscriptionHandle<T> : StreamSubscriptionHandle<T>
{
    private readonly TcpStreamPushDispatcher _dispatcher;
    private readonly TcpGatewayConnection _connection;
    private readonly Guid _handleId;

    internal TcpStreamSubscriptionHandle(
        Guid handleId,
        StreamId streamId,
        TcpStreamPushDispatcher dispatcher,
        TcpGatewayConnection connection)
    {
        _handleId = handleId;
        StreamId = streamId;
        _dispatcher = dispatcher;
        _connection = connection;
    }

    public override Guid HandleId => _handleId;
    public override StreamId StreamId { get; }

    public override async Task UnsubscribeAsync()
    {
        var headers = new MessageHeaders();
        headers.Set("sub-id", _handleId.ToString("N"));

        var envelope = new MessageEnvelope
        {
            CorrelationId = 0,
            MessageType = MessageType.StreamUnsubscribe,
            Headers = headers,
            Payload = ReadOnlyMemory<byte>.Empty
        };

        await _connection.SendOneWayAsync(envelope).ConfigureAwait(false);
        _dispatcher.Unregister(_handleId);
    }
}
