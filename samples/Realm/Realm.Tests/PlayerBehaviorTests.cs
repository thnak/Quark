using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.InMemory;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.InMemory;
using Quark.Testing.Harness;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.Content;
using Realm.GrainInterfaces;
using Realm.Grains;
using Xunit;

namespace Realm.Tests;

public sealed class PlayerBehaviorTests
{
    private static Task<TestCluster> CreateClusterAsync() =>
        TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddQuarkSerialization();
                services.AddInMemoryGrainStorage();
                services.AddMemoryStreams(RealmConstants.StreamProvider);
                services.AddStreamableCodec<DeltaBatch, DeltaBatchCodec>();
                services.AddRealmCommonCopiers();
                services.AddRealmGrainStateCopiers();

                services.AddSingleton<RealmContentLoader>();

                services.AddGrainBehavior<IWorldGrain, WorldBehavior>();

                services.AddGrainBehavior<IMapGrain, MapBehavior>();
                services.AddScoped<IActivationMemory<MapRuntime>>(sp =>
                    new ActivationMemoryAccessor<MapRuntime>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<MapRuntime>()));

                services.AddGrainBehavior<IPlayerGrain, PlayerBehavior>();
                services.AddScoped<IPersistentActivationMemory<PlayerState>>(sp =>
                    new PersistentActivationMemoryAccessor<PlayerState>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<PlayerState>(),
                        sp.GetRequiredService<IStorage<PlayerState>>(),
                        sp.GetRequiredService<ICallContext>(),
                        StorageOptions.DefaultStateName));
                services.AddScoped<IActivationMemory<PlayerRuntime>>(sp =>
                    new ActivationMemoryAccessor<PlayerRuntime>(
                        sp.GetRequiredService<IActivationShellAccessor>()
                          .Shell.GetOrCreateHolder<PlayerRuntime>()));
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorldGrain, TestWorldGrainProxy>();
                services.AddGrainProxy<IMapGrain, TestMapGrainProxy>();
                services.AddGrainProxy<IPlayerGrain, TestPlayerGrainProxy>();
            };
        });

    [Fact]
    public async Task LoginAsync_SpawnsPlayerOnStartMap()
    {
        await using TestCluster cluster = await CreateClusterAsync();

        IPlayerGrain player = cluster.Client.GetGrain<IPlayerGrain>("player-login");
        await player.LoginAsync();

        RealmContentLoader content = cluster.PrimarySilo.GetRequiredService<RealmContentLoader>();
        // WorldBehavior.LoginAsync is a deterministic pure function of playerId, so querying it
        // again with the same id reliably returns the map the player actually spawned on.
        PlayerSpawn spawn = (await cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey)
            .LoginAsync("player-login"));

        IMapGrain map = cluster.Client.GetGrain<IMapGrain>(spawn.MapId);
        MapSnapshot snap = await map.SnapshotAsync();

        Assert.Contains(snap.Entities, e => e.EntityId == "player-login");
    }

    [Fact]
    public async Task MoveAsync_UpdatesPlayerPositionOnMap()
    {
        await using TestCluster cluster = await CreateClusterAsync();

        IPlayerGrain player = cluster.Client.GetGrain<IPlayerGrain>("player-move");
        await player.LoginAsync();

        PlayerSpawn spawn = await cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey)
            .LoginAsync("player-move");
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>(spawn.MapId);

        MapSnapshot before = await map.SnapshotAsync();
        EntitySnapshot? entity = Array.Find(before.Entities, e => e.EntityId == "player-move");
        Assert.NotNull(entity);
        int startX = entity!.At.X;
        int startY = entity.At.Y;

        await player.MoveAsync(Direction.East);

        MapSnapshot after = await map.SnapshotAsync();
        EntitySnapshot? moved = Array.Find(after.Entities, e => e.EntityId == "player-move");
        Assert.NotNull(moved);
        Assert.True(moved!.At.X != startX || moved.At.Y != startY,
            "Player position should have changed after a valid move.");
    }

    [Fact]
    public async Task PersistenceRoundTrip_RestoredPositionAfterDeactivation()
    {
        await using TestCluster cluster = await CreateClusterAsync();

        const string playerId = "player-persist";
        IPlayerGrain player = cluster.Client.GetGrain<IPlayerGrain>(playerId);

        await player.LoginAsync();

        // Move east and save position.
        await player.MoveAsync(Direction.East);

        // Capture the position via the map before logout.
        PlayerSpawn spawn = await cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey)
            .LoginAsync(playerId);
        IMapGrain map = cluster.Client.GetGrain<IMapGrain>(spawn.MapId);
        MapSnapshot snapAfterMove = await map.SnapshotAsync();
        EntitySnapshot? posBeforeLogout = Array.Find(snapAfterMove.Entities, e => e.EntityId == playerId);
        Assert.NotNull(posBeforeLogout);
        int savedX = posBeforeLogout!.At.X;
        int savedY = posBeforeLogout.At.Y;

        // Logout persists state and leaves map.
        await player.LogoutAsync();

        // Force deactivation of all grains so the next login creates a fresh activation
        // that must load state from storage (simulates silo restart).
        GrainActivationTable table = cluster.PrimarySilo.GetRequiredService<GrainActivationTable>();
        await table.DisposeAsync();

        // Login again — should restore from storage and re-enter the same map at the saved coord.
        await player.LoginAsync();

        MapSnapshot snapAfterRestore = await map.SnapshotAsync();
        EntitySnapshot? restored = Array.Find(snapAfterRestore.Entities, e => e.EntityId == playerId);
        Assert.NotNull(restored);
        Assert.Equal(savedX, restored!.At.X);
        Assert.Equal(savedY, restored.At.Y);
    }

    [Fact]
    public async Task AoI_ObservesDeltasFromNeighborMapWithoutBeingOnIt()
    {
        await using TestCluster cluster = await CreateClusterAsync();

        IPlayerGrain player = cluster.Client.GetGrain<IPlayerGrain>("player-aoi");
        await player.LoginAsync();

        IWorldGrain world = cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey);
        PlayerSpawn spawn = await world.LoginAsync("player-aoi");
        MapDescriptor homeMap = await world.GetMapAsync(spawn.MapId);
        string neighborMapId = FirstNeighbor(homeMap)
            ?? throw new InvalidOperationException($"Map '{spawn.MapId}' has no neighbors to test AoI against.");

        AoiStatus initialStatus = await player.GetAoiStatusAsync();
        Assert.Contains(spawn.MapId, initialStatus.SubscribedMapIds);
        Assert.Contains(neighborMapId, initialStatus.SubscribedMapIds);

        // An entity enters the neighbor map — not the map the player is physically standing on.
        IMapGrain neighborMap = cluster.Client.GetGrain<IMapGrain>(neighborMapId);
        await neighborMap.EnterAsync("bystander", new Coord { X = 1, Y = 1 }, EntityKind.Npc);

        // The neighbor map's tick timer (100ms period) flushes the delta batch to subscribers.
        AoiStatus status = await WaitForAsync(
            () => player.GetAoiStatusAsync(),
            s => s.ReceivedDeltaCount > 0,
            TimeSpan.FromSeconds(2));

        Assert.True(status.ReceivedDeltaCount > 0);
    }

    [Fact]
    public async Task AoI_SwapsSubscriptionsOnBorderCrossTransition()
    {
        await using TestCluster cluster = await CreateClusterAsync();

        const string playerId = "player-swap";
        IPlayerGrain player = cluster.Client.GetGrain<IPlayerGrain>(playerId);
        await player.LoginAsync();

        IWorldGrain world = cluster.Client.GetGrain<IWorldGrain>(RealmConstants.WorldKey);
        PlayerSpawn spawn = await world.LoginAsync(playerId);
        MapDescriptor homeMap = await world.GetMapAsync(spawn.MapId);
        string neighborMapId = FirstNeighbor(homeMap)
            ?? throw new InvalidOperationException($"Map '{spawn.MapId}' has no neighbors to cross into.");
        Direction dir = DirectionTo(homeMap, neighborMapId);

        string? droppedCandidate = AllNeighbors(homeMap).FirstOrDefault(n => n != neighborMapId);

        IMapGrain originMap = cluster.Client.GetGrain<IMapGrain>(spawn.MapId);
        for (int i = 0; i < 25; i++)
        {
            await player.MoveAsync(dir);
            MapSnapshot snap = await originMap.SnapshotAsync();
            if (!Array.Exists(snap.Entities, e => e.EntityId == playerId))
                break;
        }

        MapSnapshot originAfter = await originMap.SnapshotAsync();
        Assert.DoesNotContain(originAfter.Entities, e => e.EntityId == playerId);

        MapDescriptor newHomeMap = await world.GetMapAsync(neighborMapId);
        string? gainedCandidate = AllNeighbors(newHomeMap).FirstOrDefault(n => n != spawn.MapId);

        AoiStatus afterStatus = await player.GetAoiStatusAsync();
        Assert.Contains(neighborMapId, afterStatus.SubscribedMapIds);
        Assert.Contains(spawn.MapId, afterStatus.SubscribedMapIds);
        if (droppedCandidate is not null)
            Assert.DoesNotContain(droppedCandidate, afterStatus.SubscribedMapIds);
        if (gainedCandidate is not null)
            Assert.Contains(gainedCandidate, afterStatus.SubscribedMapIds);
    }

    private static IEnumerable<string> AllNeighbors(MapDescriptor map)
    {
        if (map.NeighborNorth is not null) yield return map.NeighborNorth;
        if (map.NeighborSouth is not null) yield return map.NeighborSouth;
        if (map.NeighborEast is not null) yield return map.NeighborEast;
        if (map.NeighborWest is not null) yield return map.NeighborWest;
    }

    /// <summary>
    ///     Picks a crossing target preferring East, then South, then North, then West — the order
    ///     verified (by hand, against Realm.Content/Data/*.json) to walk along the map's fixed spawn
    ///     row/column without ever hitting a blocked tile for any of the 4 world maps.
    /// </summary>
    private static string? FirstNeighbor(MapDescriptor map) =>
        map.NeighborEast ?? map.NeighborSouth ?? map.NeighborNorth ?? map.NeighborWest;

    private static Direction DirectionTo(MapDescriptor map, string neighborMapId) => neighborMapId switch
    {
        _ when map.NeighborEast == neighborMapId => Direction.East,
        _ when map.NeighborSouth == neighborMapId => Direction.South,
        _ when map.NeighborNorth == neighborMapId => Direction.North,
        _ when map.NeighborWest == neighborMapId => Direction.West,
        _ => throw new InvalidOperationException($"'{neighborMapId}' is not a neighbor of '{map.Id}'.")
    };

    private static async Task<AoiStatus> WaitForAsync(
        Func<Task<AoiStatus>> poll, Func<AoiStatus, bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        AoiStatus last = await poll();
        while (!predicate(last) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            last = await poll();
        }
        return last;
    }
}

// ── Hand-written proxies ──────────────────────────────────────────────────────

file sealed class TestWorldGrainProxy : IWorldGrain, IGrainProxyActivator<TestWorldGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public TestWorldGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static TestWorldGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<PlayerSpawn> LoginAsync(string playerId)
        => _invoker.InvokeAsync<PWorld_LoginInvokable, PlayerSpawn>(
               _grainId, new PWorld_LoginInvokable(playerId)).AsTask();

    public Task<MapDescriptor> GetMapAsync(string mapId)
        => _invoker.InvokeAsync<PWorld_GetMapInvokable, MapDescriptor>(
               _grainId, new PWorld_GetMapInvokable(mapId)).AsTask();
}

file sealed class TestMapGrainProxy : IMapGrain, IGrainProxyActivator<TestMapGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public TestMapGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static TestMapGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<EnterResult> EnterAsync(string entityId, Coord at, EntityKind kind)
        => _invoker.InvokeAsync<PMap_EnterInvokable, EnterResult>(
               _grainId, new PMap_EnterInvokable(entityId, at, kind)).AsTask();

    public Task LeaveAsync(string entityId)
        => _invoker.InvokeVoidAsync(_grainId, new PMap_LeaveInvokable(entityId)).AsTask();

    public Task<MoveResult> TryMoveAsync(string entityId, Direction dir)
        => _invoker.InvokeAsync<PMap_TryMoveInvokable, MoveResult>(
               _grainId, new PMap_TryMoveInvokable(entityId, dir)).AsTask();

    public Task<MapSnapshot> SnapshotAsync()
        => _invoker.InvokeAsync<PMap_SnapshotInvokable, MapSnapshot>(
               _grainId, new PMap_SnapshotInvokable()).AsTask();
}

file sealed class TestPlayerGrainProxy : IPlayerGrain, IGrainProxyActivator<TestPlayerGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public TestPlayerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static TestPlayerGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task LoginAsync()
        => _invoker.InvokeVoidAsync(_grainId, new PPlayer_LoginInvokable()).AsTask();

    public Task MoveAsync(Direction dir)
        => _invoker.InvokeVoidAsync(_grainId, new PPlayer_MoveInvokable(dir)).AsTask();

    public Task LogoutAsync()
        => _invoker.InvokeVoidAsync(_grainId, new PPlayer_LogoutInvokable()).AsTask();

    public Task<AoiStatus> GetAoiStatusAsync()
        => _invoker.InvokeAsync<PPlayer_GetAoiStatusInvokable, AoiStatus>(
               _grainId, new PPlayer_GetAoiStatusInvokable()).AsTask();
}

// ── Invokables ────────────────────────────────────────────────────────────────

file readonly struct PWorld_LoginInvokable : IGrainInvokable<PlayerSpawn>
{
    private readonly string _playerId;
    public PWorld_LoginInvokable(string playerId) { _playerId = playerId; }
    public uint MethodId => 0u;
    public ValueTask<PlayerSpawn> Invoke(IGrainBehavior behavior)
        => new(((IWorldGrain)behavior).LoginAsync(_playerId));
    public void Serialize(ref CodecWriter writer) { }
    public PlayerSpawn DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct PWorld_GetMapInvokable : IGrainInvokable<MapDescriptor>
{
    private readonly string _mapId;
    public PWorld_GetMapInvokable(string mapId) { _mapId = mapId; }
    public uint MethodId => 1u;
    public ValueTask<MapDescriptor> Invoke(IGrainBehavior behavior)
        => new(((IWorldGrain)behavior).GetMapAsync(_mapId));
    public void Serialize(ref CodecWriter writer) { }
    public MapDescriptor DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct PMap_EnterInvokable : IGrainInvokable<EnterResult>
{
    private readonly string _entityId;
    private readonly Coord _at;
    private readonly EntityKind _kind;
    public PMap_EnterInvokable(string entityId, Coord at, EntityKind kind)
    { _entityId = entityId; _at = at; _kind = kind; }
    public uint MethodId => 0u;
    public ValueTask<EnterResult> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).EnterAsync(_entityId, _at, _kind));
    public void Serialize(ref CodecWriter writer) { }
    public EnterResult DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct PMap_LeaveInvokable : IGrainVoidInvokable
{
    private readonly string _entityId;
    public PMap_LeaveInvokable(string entityId) { _entityId = entityId; }
    public uint MethodId => 1u;
    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).LeaveAsync(_entityId));
    public void Serialize(ref CodecWriter writer) { }
}

file readonly struct PMap_TryMoveInvokable : IGrainInvokable<MoveResult>
{
    private readonly string _entityId;
    private readonly Direction _dir;
    public PMap_TryMoveInvokable(string entityId, Direction dir)
    { _entityId = entityId; _dir = dir; }
    public uint MethodId => 2u;
    public ValueTask<MoveResult> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).TryMoveAsync(_entityId, _dir));
    public void Serialize(ref CodecWriter writer) { }
    public MoveResult DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct PMap_SnapshotInvokable : IGrainInvokable<MapSnapshot>
{
    public uint MethodId => 3u;
    public ValueTask<MapSnapshot> Invoke(IGrainBehavior behavior)
        => new(((IMapGrain)behavior).SnapshotAsync());
    public void Serialize(ref CodecWriter writer) { }
    public MapSnapshot DeserializeResult(ref CodecReader reader) => new();
}

file readonly struct PPlayer_LoginInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IPlayerGrain)behavior).LoginAsync());
    public void Serialize(ref CodecWriter writer) { }
}

file readonly struct PPlayer_MoveInvokable : IGrainVoidInvokable
{
    private readonly Direction _dir;
    public PPlayer_MoveInvokable(Direction dir) { _dir = dir; }
    public uint MethodId => 1u;
    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IPlayerGrain)behavior).MoveAsync(_dir));
    public void Serialize(ref CodecWriter writer) { }
}

file readonly struct PPlayer_LogoutInvokable : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior)
        => new(((IPlayerGrain)behavior).LogoutAsync());
    public void Serialize(ref CodecWriter writer) { }
}

file readonly struct PPlayer_GetAoiStatusInvokable : IGrainInvokable<AoiStatus>
{
    public uint MethodId => 3u;
    public ValueTask<AoiStatus> Invoke(IGrainBehavior behavior)
        => new(((IPlayerGrain)behavior).GetAoiStatusAsync());
    public void Serialize(ref CodecWriter writer) { }
    public AoiStatus DeserializeResult(ref CodecReader reader) => new();
}
