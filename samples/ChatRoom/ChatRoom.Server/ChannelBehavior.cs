using ChatRoom.Common;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Streaming.Abstractions;

namespace ChatRoom.Server;

public sealed class ChannelBehavior : IGrainBehavior, IChannelGrain, IActivationLifecycle
{
    private readonly IActivationMemory<ChannelState> _memory;
    private readonly ICallContext _ctx;
    private readonly IStreamProvider? _chatProvider;

    public ChannelBehavior(
        IActivationMemory<ChannelState> memory,
        ICallContext ctx,
        [FromKeyedServices("chat")] IStreamProvider? chatProvider = null)
    {
        _memory = memory;
        _ctx = ctx;
        _chatProvider = chatProvider;
    }

    private ChannelState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct)
    {
        if (_chatProvider is not null && S.Stream is null)
        {
            S.StreamId = StreamId.Create("ChatRoom", _ctx.GrainId.Key);
            S.Stream = _chatProvider.GetStream<ChatMsg>(S.StreamId);
        }
        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public async Task<StreamId> Join(string nickname)
    {
        if (!S.Members.Contains(nickname))
            S.Members.Add(nickname);
        await PublishSystemMessageAsync($"{nickname} joined the channel.");
        return S.StreamId;
    }

    public async Task<StreamId> Leave(string nickname)
    {
        S.Members.Remove(nickname);
        await PublishSystemMessageAsync($"{nickname} left the channel.");
        return S.StreamId;
    }

    public async Task<bool> Message(ChatMsg msg)
    {
        if (S.History.Count >= 100) S.History.RemoveAt(0);
        S.History.Add(msg);
        if (S.Stream is not null)
            await S.Stream.OnNextAsync(msg);
        return true;
    }

    public Task<ChatMsg[]> ReadHistory(int numberOfMessages)
        => Task.FromResult(S.History.TakeLast(numberOfMessages).ToArray());

    public Task<string[]> GetMembers()
        => Task.FromResult(S.Members.ToArray());

    private async Task PublishSystemMessageAsync(string text)
    {
        var msg = new ChatMsg { Author = "System", Text = text, Created = DateTimeOffset.UtcNow };
        if (S.History.Count >= 100) S.History.RemoveAt(0);
        S.History.Add(msg);
        if (S.Stream is not null)
            await S.Stream.OnNextAsync(msg);
    }
}
