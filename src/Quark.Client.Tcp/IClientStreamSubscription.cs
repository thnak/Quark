using Quark.Streaming.Abstractions;

namespace Quark.Client.Tcp;

internal interface IClientStreamSubscription
{
    StreamId StreamId { get; }
    Task DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token);
    Task ErrorAsync(Exception ex);
}
