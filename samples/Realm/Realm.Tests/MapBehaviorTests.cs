using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Testing.Harness;
using Realm.Common.Dtos;
using Realm.Content;
using Realm.GrainInterfaces;
using Realm.Grains;
using Xunit;

namespace Realm.Tests;

public sealed class MapBehaviorTests
{
    private static Task<TestCluster> CreateClusterAsync() =>
        TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddSingleton<RealmContentLoader>();
                services.AddGrainBehavior<IMapGrain, MapBehavior>();
                services.AddScoped<IActivationMemory<MapRuntime>>(sp =>
                    new ActivationMemoryAccessor<MapRuntime>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<MapRuntime>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IMapGrain, MapGrainProxy>();
            };
        });

    [Fact]
    public async Task EnterAsync_AddsEntityToRoster()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        EnterResult result = await map.EnterAsync("p1", new Coord { X = 3, Y = 3 }, EntityKind.Player);

        Assert.True(result.Success);
        MapSnapshot snap = await map.SnapshotAsync();
        Assert.Single(snap.Entities);
        Assert.Equal("p1", snap.Entities[0].EntityId);
        Assert.Equal(3, snap.Entities[0].At.X);
        Assert.Equal(3, snap.Entities[0].At.Y);
    }

    [Fact]
    public async Task LeaveAsync_RemovesEntityFromRoster()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        await map.EnterAsync("p1", new Coord { X = 3, Y = 3 }, EntityKind.Player);
        await map.LeaveAsync("p1");

        MapSnapshot snap = await map.SnapshotAsync();
        Assert.Empty(snap.Entities);
    }

    [Fact]
    public async Task TryMoveAsync_ValidMove_UpdatesPosition()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        await map.EnterAsync("p1", new Coord { X = 3, Y = 3 }, EntityKind.Player);
        MoveResult result = await map.TryMoveAsync("p1", Direction.East);

        Assert.True(result.Success);
        Assert.NotNull(result.NewCoord);
        Assert.Equal(4, result.NewCoord!.X);
        Assert.Equal(3, result.NewCoord!.Y);
        Assert.Null(result.TransitionMapId);

        MapSnapshot snap = await map.SnapshotAsync();
        Assert.Equal(4, snap.Entities[0].At.X);
        Assert.Equal(3, snap.Entities[0].At.Y);
    }

    [Fact]
    public async Task TryMoveAsync_BlockedTile_Fails()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        // map-nw blocked: [5,5]=[row=5,col=5] → (X=5,Y=5); move East from (4,5) hits (5,5)
        await map.EnterAsync("p1", new Coord { X = 4, Y = 5 }, EntityKind.Player);
        MoveResult result = await map.TryMoveAsync("p1", Direction.East);

        Assert.False(result.Success);

        MapSnapshot snap = await map.SnapshotAsync();
        Assert.Equal(4, snap.Entities[0].At.X);
        Assert.Equal(5, snap.Entities[0].At.Y);
    }

    [Fact]
    public async Task TryMoveAsync_OccupiedTile_Fails()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        await map.EnterAsync("p1", new Coord { X = 3, Y = 3 }, EntityKind.Player);
        await map.EnterAsync("p2", new Coord { X = 4, Y = 3 }, EntityKind.Player);

        MoveResult result = await map.TryMoveAsync("p1", Direction.East);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task TryMoveAsync_OutOfBoundsNoNeighbor_Fails()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        // map-nw has no West neighbor
        await map.EnterAsync("p1", new Coord { X = 0, Y = 5 }, EntityKind.Player);
        MoveResult result = await map.TryMoveAsync("p1", Direction.West);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task TryMoveAsync_BorderCross_ReturnsTransitionInfo()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        // map-nw is 20x20; East neighbor is map-ne
        // Entity at X=19 moves East → crosses border
        await map.EnterAsync("p1", new Coord { X = 19, Y = 7 }, EntityKind.Player);
        MoveResult result = await map.TryMoveAsync("p1", Direction.East);

        Assert.True(result.Success);
        Assert.Equal("map-ne", result.TransitionMapId);
        Assert.NotNull(result.TransitionCoord);
        Assert.Equal(0, result.TransitionCoord!.X);
        Assert.Equal(7, result.TransitionCoord!.Y);
    }

    [Fact]
    public async Task TryMoveAsync_BorderCrossSouth_ReturnsTransitionInfo()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        // map-nw is 20x20; South neighbor is map-sw
        // Entity at Y=19 moves South → crosses border
        await map.EnterAsync("p1", new Coord { X = 5, Y = 19 }, EntityKind.Player);
        MoveResult result = await map.TryMoveAsync("p1", Direction.South);

        Assert.True(result.Success);
        Assert.Equal("map-sw", result.TransitionMapId);
        Assert.NotNull(result.TransitionCoord);
        Assert.Equal(5, result.TransitionCoord!.X);
        Assert.Equal(0, result.TransitionCoord!.Y);
    }

    [Fact]
    public async Task SnapshotAsync_ReflectsCurrentRoster()
    {
        await using TestCluster cluster = await CreateClusterAsync();
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>("map-nw");

        await map.EnterAsync("p1", new Coord { X = 1, Y = 1 }, EntityKind.Player);
        await map.EnterAsync("p2", new Coord { X = 2, Y = 2 }, EntityKind.Player);

        MapSnapshot snap = await map.SnapshotAsync();

        Assert.Equal("map-nw", snap.MapId);
        Assert.Equal(2, snap.Entities.Length);
        Assert.Contains(snap.Entities, e => e.EntityId == "p1");
        Assert.Contains(snap.Entities, e => e.EntityId == "p2");
    }
}

// ── Hand-written proxy ────────────────────────────────────────────────────────

file sealed class MapGrainProxy : IMapGrain, IGrainProxyActivator<MapGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public MapGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static MapGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<EnterResult> EnterAsync(string entityId, Coord at, EntityKind kind)
        => _invoker.InvokeAsync<MapBehavior_EnterInvokable, EnterResult>(
               _grainId, new MapBehavior_EnterInvokable(entityId, at, kind));

    public Task LeaveAsync(string entityId)
        => _invoker.InvokeVoidAsync(_grainId, new MapBehavior_LeaveInvokable(entityId));

    public Task<MoveResult> TryMoveAsync(string entityId, Direction dir)
        => _invoker.InvokeAsync<MapBehavior_TryMoveInvokable, MoveResult>(
               _grainId, new MapBehavior_TryMoveInvokable(entityId, dir));

    public Task<MapSnapshot> SnapshotAsync()
        => _invoker.InvokeAsync<MapBehavior_SnapshotInvokable, MapSnapshot>(
               _grainId, new MapBehavior_SnapshotInvokable());
}

// ── Invokables ────────────────────────────────────────────────────────────────

file readonly struct MapBehavior_EnterInvokable : IGrainInvokable<EnterResult>
{
    private readonly string _entityId;
    private readonly Coord _at;
    private readonly EntityKind _kind;

    public MapBehavior_EnterInvokable(string entityId, Coord at, EntityKind kind)
    {
        _entityId = entityId;
        _at = at;
        _kind = kind;
    }

    public uint MethodId => 0u;

    public ValueTask<EnterResult> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).EnterAsync(_entityId, _at, _kind));

    public void Serialize(ref CodecWriter writer) { }

    public EnterResult DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct MapBehavior_LeaveInvokable : IGrainVoidInvokable
{
    private readonly string _entityId;

    public MapBehavior_LeaveInvokable(string entityId) { _entityId = entityId; }

    public uint MethodId => 1u;

    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).LeaveAsync(_entityId));

    public void Serialize(ref CodecWriter writer) { }
}

file readonly struct MapBehavior_TryMoveInvokable : IGrainInvokable<MoveResult>
{
    private readonly string _entityId;
    private readonly Direction _dir;

    public MapBehavior_TryMoveInvokable(string entityId, Direction dir)
    {
        _entityId = entityId;
        _dir = dir;
    }

    public uint MethodId => 2u;

    public ValueTask<MoveResult> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).TryMoveAsync(_entityId, _dir));

    public void Serialize(ref CodecWriter writer) { }

    public MoveResult DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct MapBehavior_SnapshotInvokable : IGrainInvokable<MapSnapshot>
{
    public uint MethodId => 3u;

    public ValueTask<MapSnapshot> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).SnapshotAsync());

    public void Serialize(ref CodecWriter writer) { }

    public MapSnapshot DeserializeResult(ref CodecReader reader) => new();
}
