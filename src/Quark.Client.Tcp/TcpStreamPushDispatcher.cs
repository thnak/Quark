using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;
using Quark.Transport.Abstractions;

namespace Quark.Client.Tcp;

/// <summary>
///     Maintains the client-side subscription table and routes incoming
///     <see cref="MessageType.StreamPush" /> envelopes to the correct
///     <see cref="IClientStreamSubscription" />.
/// </summary>
public sealed class TcpStreamPushDispatcher
{
    private readonly ConcurrentDictionary<Guid, IClientStreamSubscription> _subscriptions = new();

    internal void Register(Guid subId, IClientStreamSubscription sub) => _subscriptions[subId] = sub;

    public void Unregister(Guid subId) => _subscriptions.TryRemove(subId, out _);

    internal IReadOnlyList<IClientStreamSubscription> GetForStream(StreamId streamId)
        => _subscriptions.Values.Where(s => s.StreamId.Equals(streamId)).ToList();

    public async Task DispatchAsync(MessageEnvelope envelope)
    {
        var subIdStr = envelope.Headers?.Get("sub-id");
        if (subIdStr is null) return;
        if (!Guid.TryParse(subIdStr, out var subId)) return;
        if (!_subscriptions.TryGetValue(subId, out var sub)) return;

        var seqStr = envelope.Headers?.Get("seq");
        var seq = long.TryParse(seqStr, out var s) ? s : 0L;
        var token = new SequentialToken(seq);

        await sub.DispatchAsync(envelope.Payload, token).ConfigureAwait(false);
    }
}
