using ChatRoom.Client;
using ChatRoom.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization;
using Quark.Streaming.Abstractions;
using Spectre.Console;

var host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway(30002);
        client.AddTcpClientStreams("chat");
        client.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
        client.Services.AddGrainProxy<IChannelGrain, ChannelGrainProxy>();
    })
    .Build();

await host.StartAsync();

var clusterClient = host.Services.GetRequiredService<IClusterClient>();
await ((TcpGatewayClusterClient)clusterClient).Connect();

var streamProvider = clusterClient.GetStreamProvider("chat");

var username = AnsiConsole.Ask<string>("Enter your [green]username[/]:");
string? currentChannel = null;
IChannelGrain? currentGrain = null;
var subscriptionHandles = new List<StreamSubscriptionHandle<ChatMsg>>();

AnsiConsole.MarkupLine("[grey]Commands: /j <channel>, /l, /h, /m, /n <name>, /exit[/]");

while (true)
{
    var input = Console.ReadLine();
    if (input is null) break;

    if (input.StartsWith("/j "))
    {
        var channel = input[3..].Trim();
        if (currentGrain is not null)
        {
            await currentGrain.Leave(username);
            foreach (var h in subscriptionHandles) await h.UnsubscribeAsync();
            subscriptionHandles.Clear();
        }
        currentChannel = channel;
        currentGrain = clusterClient.GetGrain<IChannelGrain>(channel);
        var streamId = await currentGrain.Join(username);
        var stream = streamProvider.GetStream<ChatMsg>(streamId);
        var handle = await stream.SubscribeAsync(new StreamObserver(channel));
        subscriptionHandles.Add(handle);
        AnsiConsole.MarkupLine($"[green]Joined #{channel}[/]");
    }
    else if (input == "/l")
    {
        if (currentGrain is not null && currentChannel is not null)
        {
            await currentGrain.Leave(username);
            foreach (var h in subscriptionHandles) await h.UnsubscribeAsync();
            subscriptionHandles.Clear();
            AnsiConsole.MarkupLine($"[yellow]Left #{currentChannel}[/]");
            currentGrain = null;
            currentChannel = null;
        }
    }
    else if (input == "/h")
    {
        if (currentGrain is null) { AnsiConsole.MarkupLine("[red]Not in a channel.[/]"); continue; }
        var history = await currentGrain.ReadHistory(50);
        foreach (var msg in history)
            AnsiConsole.MarkupLine($"[grey]{msg.Created:HH:mm:ss}[/] [bold]{Markup.Escape(msg.Author)}:[/] {Markup.Escape(msg.Text)}");
    }
    else if (input == "/m")
    {
        if (currentGrain is null) { AnsiConsole.MarkupLine("[red]Not in a channel.[/]"); continue; }
        var members = await currentGrain.GetMembers();
        var table = new Table().AddColumn("Members");
        foreach (var m in members) table.AddRow(Markup.Escape(m));
        AnsiConsole.Write(table);
    }
    else if (input.StartsWith("/n "))
    {
        username = input[3..].Trim();
        AnsiConsole.MarkupLine($"[grey]Username changed to {Markup.Escape(username)}[/]");
    }
    else if (input == "/exit")
    {
        if (currentGrain is not null)
        {
            await currentGrain.Leave(username);
            foreach (var h in subscriptionHandles) await h.UnsubscribeAsync();
        }
        break;
    }
    else if (currentGrain is not null)
    {
        await currentGrain.Message(new ChatMsg { Author = username, Text = input, Created = DateTimeOffset.UtcNow });
    }
    else
    {
        AnsiConsole.MarkupLine("[red]Join a channel first with /j <channel>[/]");
    }
}

await host.StopAsync();
