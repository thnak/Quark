using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Core;
using Quark.Diagnostics;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Streaming.InMemory;
using Quark.Transport.Tcp;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.Content;
using Realm.Grains;
using Realm.Server;

IHost host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30010);

        silo.Services.AddInMemoryGrainStorage();
        silo.Services.AddMemoryStreams(RealmConstants.StreamProvider);
        silo.Services.AddStreamableCodec<DeltaBatch, DeltaBatchCodec>();

        silo.Services.AddRealmContent();
        silo.Services.AddRealmCommonCopiers();
        silo.Services.AddRealmGrainStateCopiers();
        silo.Services.AddRealmGrainsBehaviors();

        silo.Services.AddQuarkDiagnostics<RealmDiagnosticsListener>();
        silo.Services.AddQuarkStuckGrainDetector();
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
