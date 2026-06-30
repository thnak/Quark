using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Testing.Harness;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.Content;
using Realm.GrainInterfaces;
using Realm.Grains;
using Xunit;

namespace Realm.Tests;

public sealed class WorldBehaviorTests
{
    private static Task<TestCluster> CreateClusterAsync() =>
        TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddSingleton<RealmContentLoader>();
                services.AddGrainBehavior<IWorldGrain, WorldBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorldGrain, WorldGrainProxy>();
            };
        });

    [Fact]
    public async Task LoginAsync_ReturnsValidSpawnOnRealMap()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IWorldGrain world = cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey);

        PlayerSpawn spawn = await world.LoginAsync("player-1");

        Assert.NotEmpty(spawn.MapId);
        Assert.True(spawn.At.X >= 0);
        Assert.True(spawn.At.Y >= 0);

        RealmContentLoader content = cluster.PrimarySilo.GetRequiredService<RealmContentLoader>();
        Assert.NotNull(content.GetMap(spawn.MapId));
    }

    [Fact]
    public async Task GetMapAsync_ReturnsCorrectDimensionsAndNeighbors()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IWorldGrain world = cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey);

        MapDescriptor desc = await world.GetMapAsync("map-nw");

        Assert.Equal("map-nw", desc.Id);
        Assert.Equal(20, desc.Width);
        Assert.Equal(20, desc.Height);
        Assert.Null(desc.NeighborNorth);
        Assert.Null(desc.NeighborWest);
        Assert.Equal("map-ne", desc.NeighborEast);
        Assert.Equal("map-sw", desc.NeighborSouth);
    }
}

// ── Hand-written proxy ────────────────────────────────────────────────────────

file sealed class WorldGrainProxy : IWorldGrain, IGrainProxyActivator<WorldGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public WorldGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static WorldGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<PlayerSpawn> LoginAsync(string playerId)
        => _invoker.InvokeAsync<WorldBehavior_LoginAsyncInvokable, PlayerSpawn>(
               _grainId, new WorldBehavior_LoginAsyncInvokable(playerId));

    public Task<MapDescriptor> GetMapAsync(string mapId)
        => _invoker.InvokeAsync<WorldBehavior_GetMapAsyncInvokable, MapDescriptor>(
               _grainId, new WorldBehavior_GetMapAsyncInvokable(mapId));
}

// ── Invokables ────────────────────────────────────────────────────────────────

file readonly struct WorldBehavior_LoginAsyncInvokable : IGrainInvokable<PlayerSpawn>
{
    private readonly string _playerId;

    public WorldBehavior_LoginAsyncInvokable(string playerId) { _playerId = playerId; }

    public uint MethodId => 0u;

    public ValueTask<PlayerSpawn> Invoke(IGrainBehavior behavior)
        => new(((IWorldGrain)behavior).LoginAsync(_playerId));

    public void Serialize(ref CodecWriter writer) { }

    public PlayerSpawn DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct WorldBehavior_GetMapAsyncInvokable : IGrainInvokable<MapDescriptor>
{
    private readonly string _mapId;

    public WorldBehavior_GetMapAsyncInvokable(string mapId) { _mapId = mapId; }

    public uint MethodId => 1u;

    public ValueTask<MapDescriptor> Invoke(IGrainBehavior behavior)
        => new(((IWorldGrain)behavior).GetMapAsync(_mapId));

    public void Serialize(ref CodecWriter writer) { }

    public MapDescriptor DeserializeResult(ref CodecReader reader) => new();
}
