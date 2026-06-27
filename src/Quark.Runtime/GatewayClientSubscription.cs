using System.Buffers;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Server-side <see cref="IUntypedStreamObserver" /> that serializes each stream item
///     and pushes the encoded bytes to a remote client connection.
/// </summary>
public sealed class GatewayClientSubscription : IUntypedStreamObserver
{
    public Guid SubId { get; }
    public StreamId StreamId { get; }

    private readonly ICodecProvider _codecs;
    private readonly Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task> _push;

    public GatewayClientSubscription(
        Guid subId,
        StreamId streamId,
        ICodecProvider codecs,
        Func<ReadOnlyMemory<byte>, StreamSequenceToken?, Task> push)
    {
        SubId = subId;
        StreamId = streamId;
        _codecs = codecs;
        _push = push;
    }

    public async Task OnNextAsync(object item, StreamSequenceToken? token)
    {
        IGeneralizedCodec codec = _codecs.TryGetGeneralizedCodec(item.GetType())
                                  ?? throw new InvalidOperationException(
                                      $"No IGeneralizedCodec registered for {item.GetType().Name}");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        codec.WriteField(writer, 0, item.GetType(), item);

        await _push(buffer.WrittenMemory, token).ConfigureAwait(false);
    }

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    public Task OnCompletedAsync() => Task.CompletedTask;
}
