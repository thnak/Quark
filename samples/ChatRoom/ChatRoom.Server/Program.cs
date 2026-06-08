using ChatRoom.Common;
using ChatRoom.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Core;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Streaming.InMemory;
using Quark.Transport.Tcp;

var host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(siloPort: 11112, gatewayPort: 30002);
        silo.Services.AddMemoryStreams("chat");
        silo.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();

        silo.Services.AddGrain<ChannelGrain>();
        silo.Services.AddGrainActivatorFactory<ChannelGrainActivatorFactory>();
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("ChannelGrain"),
            new ChannelGrainProxy_TransportDispatcher());
    })
    .UseQuarkClient(client =>
    {
        client.Services.AddLocalClusterClient();
        client.Services.AddGrainProxy<IChannelGrain, ChannelGrainProxy>();
    })
    .Build();

await host.StartAsync();
Console.WriteLine("ChatRoom server ready on port 30002. Press Ctrl+C to stop.");
await host.WaitForShutdownAsync();
