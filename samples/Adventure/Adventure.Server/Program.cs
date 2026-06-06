using Adventure.GrainInterfaces;
using Adventure.Grains;
using Adventure.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Core;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Hosting;
using Quark.Client;
using Quark.Runtime;

var host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.UseLocalhostClustering();

        silo.Services.AddGrain<PlayerGrain>();
        silo.Services.AddGrainActivatorFactory<PlayerGrainActivatorFactory>();
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("PlayerGrain"),
            new PlayerGrainProxy_TransportDispatcher());

        silo.Services.AddGrain<RoomGrain>();
        silo.Services.AddGrainActivatorFactory<RoomGrainActivatorFactory>();
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("RoomGrain"),
            new RoomGrainProxy_TransportDispatcher());

        silo.Services.AddGrain<MonsterGrain>();
        silo.Services.AddGrainActivatorFactory<MonsterGrainActivatorFactory>();
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("MonsterGrain"),
            new MonsterGrainProxy_TransportDispatcher());
    })
    .UseQuarkClient(client =>
    {
        client.Services.AddLocalClusterClient();
        client.Services.AddGrainProxy<IPlayerGrain, PlayerGrainProxy>();
        client.Services.AddGrainProxy<IRoomGrain, RoomGrainProxy>();
        client.Services.AddGrainProxy<IMonsterGrain, MonsterGrainProxy>();
    })
    .Build();

await host.StartAsync();

var grainFactory = host.Services.GetRequiredService<IGrainFactory>();

var mapFile = Path.Combine(AppContext.BaseDirectory, "AdventureMap.json");
if (!File.Exists(mapFile))
    mapFile = Path.Combine(
        Path.GetDirectoryName(typeof(Program).Assembly.Location)!,
        "..", "..", "..", "..", "AdventureMap.json");

await AdventureGame.LoadAsync(grainFactory, mapFile);
Console.WriteLine("Adventure server ready on port 30000. Press Ctrl+C to stop.");

await host.WaitForShutdownAsync();
