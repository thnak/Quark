using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Core;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Streaming.InMemory;
using Quark.Transport.Tcp;
using Realm.Common;
using Realm.Content;
using Realm.Grains;

IHost host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30010);

        silo.Services.AddInMemoryGrainStorage();
        silo.Services.AddMemoryStreams(RealmConstants.StreamProvider);

        silo.Services.AddRealmContent();
        silo.Services.AddRealmCommonCopiers();
        silo.Services.AddRealmGrainStateCopiers();
        silo.Services.AddRealmGrainsBehaviors();
    })
    .UseQuarkClient(client =>
    {
        client.Services.AddLocalClusterClient();
        client.Services.AddRealmGrainInterfacesGrainProxies();
    })
    .Build();

await host.StartAsync();
Console.WriteLine("Realm server ready on port 30010. Press Ctrl+C to stop.");
await host.WaitForShutdownAsync();
