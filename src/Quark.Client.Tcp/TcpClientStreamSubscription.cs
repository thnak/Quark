using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

internal sealed class TcpClientStreamSubscription<T> : IClientStreamSubscription
{
    public Guid SubId { get; }
    public StreamId StreamId { get; }

    private IAsyncObserver<T> _observer;
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

    public void SetObserver(IAsyncObserver<T> observer) => _observer = observer;

    public async Task DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token)
    {
        var reader = new CodecReader(payload);
        var field = reader.ReadFieldHeader();
        var item = _codec.ReadValue(reader, field);
        await _observer.OnNextAsync(item!, token).ConfigureAwait(false);
    }

    public Task ErrorAsync(Exception ex) => _observer.OnErrorAsync(ex);
}
