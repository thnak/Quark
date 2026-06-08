using ChatRoom.Common;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Streaming.Abstractions;

namespace ChatRoom.Server;

public sealed class ChannelGrain : Grain, IChannelGrain
{
    private readonly List<ChatMsg> _history = new();
    private readonly List<string> _members = new();
    private IAsyncStream<ChatMsg>? _stream;
    private StreamId _streamId;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var provider = ServiceProvider.GetRequiredKeyedService<IStreamProvider>("chat");
        _streamId = StreamId.Create("ChatRoom", this.GetPrimaryKeyString());
        _stream = provider.GetStream<ChatMsg>(_streamId);
        return Task.CompletedTask;
    }

    public async Task<StreamId> Join(string nickname)
    {
        if (!_members.Contains(nickname))
            _members.Add(nickname);
        await PublishSystemMessageAsync($"{nickname} joined the channel.");
        return _streamId;
    }

    public async Task<StreamId> Leave(string nickname)
    {
        _members.Remove(nickname);
        await PublishSystemMessageAsync($"{nickname} left the channel.");
        return _streamId;
    }

    public async Task<bool> Message(ChatMsg msg)
    {
        if (_history.Count >= 100) _history.RemoveAt(0);
        _history.Add(msg);
        await _stream!.OnNextAsync(msg);
        return true;
    }

    public Task<ChatMsg[]> ReadHistory(int numberOfMessages)
        => Task.FromResult(_history.TakeLast(numberOfMessages).ToArray());

    public Task<string[]> GetMembers()
        => Task.FromResult(_members.ToArray());

    private async Task PublishSystemMessageAsync(string text)
    {
        var msg = new ChatMsg { Author = "System", Text = text, Created = DateTimeOffset.UtcNow };
        if (_history.Count >= 100) _history.RemoveAt(0);
        _history.Add(msg);
        await _stream!.OnNextAsync(msg);
    }
}
