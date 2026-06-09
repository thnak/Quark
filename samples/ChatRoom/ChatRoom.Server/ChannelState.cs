using ChatRoom.Common;
using Quark.Streaming.Abstractions;

namespace ChatRoom.Server;

public sealed class ChannelState
{
    public List<ChatMsg> History { get; } = [];
    public List<string> Members { get; } = [];
    public IAsyncStream<ChatMsg>? Stream { get; set; }
    public StreamId StreamId { get; set; }
}