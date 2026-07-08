using ChatRoom.Common;
using Quark.Streaming.Abstractions;
using Spectre.Console;

namespace ChatRoom.Client;

public sealed class StreamObserver : IAsyncObserver<ChatMsg>
{
    private readonly string _channelName;

    public StreamObserver(string channelName) => _channelName = channelName;

    public ValueTask OnNextAsync(ChatMsg item, StreamSequenceToken? token)
    {
        AnsiConsole.MarkupLine(
            $"[grey][[{item.Created:HH:mm:ss}]][/][green][[{_channelName}]][/] [bold]{Markup.Escape(item.Author)}:[/] {Markup.Escape(item.Text)}");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(Exception ex)
    {
        AnsiConsole.WriteException(ex);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
}
