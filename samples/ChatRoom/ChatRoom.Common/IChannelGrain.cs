using Quark.Core.Abstractions.Grains;
using Quark.Streaming.Abstractions;

namespace ChatRoom.Common;

public interface IChannelGrain : IGrainWithStringKey
{
    Task<StreamId> Join(string nickname);
    Task<StreamId> Leave(string nickname);
    Task<bool> Message(ChatMsg msg);
    Task<ChatMsg[]> ReadHistory(int numberOfMessages);
    Task<string[]> GetMembers();
}
