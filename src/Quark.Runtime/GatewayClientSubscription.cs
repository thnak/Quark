using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    // IL3051/IL2046: intentional mismatch — IUntypedStreamObserver is a non-typed escape hatch;
    // only this gateway implementation performs dynamic type resolution. Other implementations
    // are free to avoid GetType(). Callers of the interface itself are safe.
#pragma warning disable IL3051, IL2046
    [RequiresDynamicCode(
        "Stream item codec resolution uses object.GetType() at runtime, which is not supported in Native AOT. " +
        "Use typed IAsyncObserver<T> subscriptions instead.")]
    [RequiresUnreferencedCode(
        "Stream item type may be trimmed by the linker. " +
        "Use typed IAsyncObserver<T> subscriptions for AOT-safe streaming.")]
    public async Task OnNextAsync(object item, StreamSequenceToken? token)
#pragma warning restore IL3051, IL2046
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new NotSupportedException(
                $"{nameof(GatewayClientSubscription)}.{nameof(OnNextAsync)} requires dynamic code (JIT). " +
                "Use typed stream subscriptions in Native AOT contexts.");

        Type itemType = item.GetType();
        IGeneralizedCodec codec = _codecs.TryGetGeneralizedCodec(itemType)
                                  ?? throw new InvalidOperationException(
                                      $"No IGeneralizedCodec registered for {itemType.Name}");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        codec.WriteField(writer, 0, itemType, item);

        await _push(buffer.WrittenMemory, token).ConfigureAwait(false);
    }

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    public Task OnCompletedAsync() => Task.CompletedTask;
}
