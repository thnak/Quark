using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

internal interface IClientStreamSubscription
{
    StreamId StreamId { get; }
    ValueTask DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token);
    ValueTask ErrorAsync(Exception ex); // TODO did not used
}
