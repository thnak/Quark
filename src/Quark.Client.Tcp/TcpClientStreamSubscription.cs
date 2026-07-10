using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

internal sealed class TcpClientStreamSubscription<T> : IClientStreamSubscription
{
    public Guid SubId { get; }
    public StreamId StreamId { get; }

    private readonly IAsyncObserver<T> _observer;
    private readonly IFieldCodec<T> _codec;

    public TcpClientStreamSubscription(
        Guid subId,
        StreamId streamId,
        IAsyncObserver<T> observer,
        IFieldCodec<T> codec)
    {
        SubId = subId;
        StreamId = streamId;
        _observer = observer;
        _codec = codec;
    }

    public async ValueTask DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token)
    {
        var reader = new CodecReader(payload);
        Field field = reader.ReadFieldHeader();
        T item = _codec.ReadValue(reader, field);
        await _observer.OnNextAsync(item!, token).ConfigureAwait(false);
    }

    public ValueTask ErrorAsync(Exception ex) => _observer.OnErrorAsync(ex);
}
