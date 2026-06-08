using Adventure.GrainInterfaces;
using Adventure.Grains;
using Adventure.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Client;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Transport.Tcp;

var host = Host.CreateDefaultBuilder(args)
    .UseQuark(silo =>
    {
        silo.Services.AddQuarkRuntime();
        silo.Services.AddTcpTransport();
        silo.UseLocalhostClustering(gatewayPort: 30001);

        silo.Services.AddGrainBehavior<IPlayerGrain, PlayerBehavior>();
        silo.Services.AddGrainBehavior<IRoomGrain, RoomBehavior>();
        silo.Services.AddGrainBehavior<IMonsterGrain, MonsterBehavior>();

        silo.Services.AddGrainTransportDispatcher(
            new GrainType("PlayerGrain"),
            new PlayerGrainProxy_TransportDispatcher());
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("RoomGrain"),
            new RoomGrainProxy_TransportDispatcher());
        silo.Services.AddGrainTransportDispatcher(
            new GrainType("MonsterGrain"),
            new MonsterGrainProxy_TransportDispatcher());

        // Scoped IActivationMemory<T> registrations — one per distinct TState
        silo.Services.AddScoped<IActivationMemory<PlayerState>>(sp =>
            new ActivationMemoryAccessor<PlayerState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<PlayerState>()));
        silo.Services.AddScoped<IActivationMemory<RoomState>>(sp =>
            new ActivationMemoryAccessor<RoomState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<RoomState>()));
        silo.Services.AddScoped<IActivationMemory<MonsterState>>(sp =>
            new ActivationMemoryAccessor<MonsterState>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateHolder<MonsterState>()));
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
Console.WriteLine("Adventure server ready on port 30001. Press Ctrl+C to stop.");

await host.WaitForShutdownAsync();
