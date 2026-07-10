using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;
using Quark.Persistence.Abstractions;
using Quark.Runtime;
using Quark.Streaming.Abstractions;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.GrainInterfaces;

namespace Realm.Grains;

[PreferLocalPlacement]
public sealed class PlayerBehavior : IGrainBehavior, IPlayerGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<PlayerState> _memory;
    private readonly IActivationMemory<PlayerRuntime> _runtime;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;
    private readonly IActivationShellAccessor _shell;
    private readonly IStreamProvider? _streamProvider;

    public PlayerBehavior(
        IPersistentActivationMemory<PlayerState> memory,
        IActivationMemory<PlayerRuntime> runtime,
        IGrainFactory factory,
        ICallContext ctx,
        IActivationShellAccessor shell,
        [FromKeyedServices(RealmConstants.StreamProvider)] IStreamProvider? streamProvider = null)
    {
        _memory = memory;
        _runtime = runtime;
        _factory = factory;
        _ctx = ctx;
        _shell = shell;
        _streamProvider = streamProvider;
    }

    private PlayerState S => _memory.Value;
    private PlayerRuntime Rt => _runtime.Value;
    private string PlayerKey => _ctx.GrainId.Key;

    public Task OnActivateAsync(CancellationToken ct) => _memory.LoadAsync(ct);

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        => Task.CompletedTask;

    public async Task LoginAsync()
    {
        if (string.IsNullOrEmpty(S.MapId))
        {
            IWorldGrain world = _factory.GetGrain<IWorldGrain>(RealmConstants.WorldKey);
            PlayerSpawn spawn = await world.LoginAsync(PlayerKey);
            S.MapId = spawn.MapId;
            S.At = spawn.At;
            await _memory.SaveAsync();
        }

        IMapGrain map = _factory.GetGrain<IMapGrain>(S.MapId);
        await map.EnterAsync(PlayerKey, S.At, EntityKind.Player);
        await SyncAoiAsync(S.MapId);
    }

    public async Task MoveAsync(Direction dir)
    {
        if (string.IsNullOrEmpty(S.MapId))
            return;

        IMapGrain map = _factory.GetGrain<IMapGrain>(S.MapId);
        MoveResult result = await map.TryMoveAsync(PlayerKey, dir);

        if (!result.Success)
            return;

        if (result.TransitionMapId is not null)
        {
            await map.LeaveAsync(PlayerKey);

            IMapGrain nextMap = _factory.GetGrain<IMapGrain>(result.TransitionMapId);
            Coord entryCoord = result.TransitionCoord ?? new Coord();
            await nextMap.EnterAsync(PlayerKey, entryCoord, EntityKind.Player);

            S.MapId = result.TransitionMapId;
            S.At = entryCoord;
            await _memory.SaveAsync();
            await SyncAoiAsync(S.MapId);
            return;
        }

        if (result.NewCoord is not null)
        {
            S.At = result.NewCoord;
            await _memory.SaveAsync();
        }
    }

    public async Task LogoutAsync()
    {
        if (!string.IsNullOrEmpty(S.MapId))
        {
            IMapGrain map = _factory.GetGrain<IMapGrain>(S.MapId);
            await map.LeaveAsync(PlayerKey);
        }

        await UnsubscribeAllAsync();
        await _memory.SaveAsync();
    }

    public Task<AoiStatus> GetAoiStatusAsync() => Task.FromResult(new AoiStatus
    {
        SubscribedMapIds = [.. Rt.Subscriptions.Keys],
        ReceivedDeltaCount = Rt.ReceivedDeltaCount
    });

    /// <summary>
    ///     Recomputes the AoI map set (the map the player is on plus its cardinal
    ///     neighbors — the content model only tracks N/S/E/W links, not diagonals) and
    ///     subscribes/unsubscribes the player's map-delta streams to match.
    /// </summary>
    private async Task SyncAoiAsync(string mapId)
    {
        if (_streamProvider is null)
            return;

        MapDescriptor desc = await _factory.GetGrain<IWorldGrain>(RealmConstants.WorldKey).GetMapAsync(mapId);
        var target = new HashSet<string>(StringComparer.Ordinal) { mapId };
        if (desc.NeighborNorth is not null) target.Add(desc.NeighborNorth);
        if (desc.NeighborSouth is not null) target.Add(desc.NeighborSouth);
        if (desc.NeighborEast is not null) target.Add(desc.NeighborEast);
        if (desc.NeighborWest is not null) target.Add(desc.NeighborWest);

        PlayerRuntime rt = Rt;

        foreach (string staleMapId in rt.Subscriptions.Keys.Where(k => !target.Contains(k)).ToArray())
        {
            await rt.Subscriptions[staleMapId].UnsubscribeAsync();
            rt.Subscriptions.Remove(staleMapId);
        }

        foreach (string targetMapId in target)
        {
            if (rt.Subscriptions.ContainsKey(targetMapId))
                continue;

            // Subscribe before snapshotting so an entity that enters between the two calls is
            // seen (as a delta, possibly duplicating part of the snapshot) rather than missed.
            IAsyncStream<DeltaBatch> stream = _streamProvider.GetStream<DeltaBatch>(
                StreamId.Create(RealmConstants.MapStreamNamespace, targetMapId));
            rt.Subscriptions[targetMapId] = await stream.SubscribeAsync(new AoiObserver(_shell.Shell, rt));

            MapSnapshot snapshot = await _factory.GetGrain<IMapGrain>(targetMapId).SnapshotAsync();
            rt.ReceivedDeltaCount += snapshot.Entities.Length;
        }
    }

    private async Task UnsubscribeAllAsync()
    {
        PlayerRuntime rt = Rt;
        foreach (StreamSubscriptionHandle<DeltaBatch> handle in rt.Subscriptions.Values)
            await handle.UnsubscribeAsync();
        rt.Subscriptions.Clear();
    }

    /// <summary>
    ///     Delivery for a subscribed map's stream runs on the publishing grain's own call
    ///     stack (see StreamSubscriptionRegistry.PublishAsync), typically from inside that
    ///     map's own tick-timer callback — i.e. already inside the map's mailbox. Marshal the
    ///     state mutation through <see cref="GrainActivation.PostAsync{TState}" /> — the same
    ///     mechanism grain timers use — so it is serialized against this player's own calls
    ///     instead of racing them. Crucially, do NOT await that post here: PostAsync only
    ///     returns once this player's mailbox has drained the item, and if this player happens
    ///     to be mid-call to the very map that's publishing (e.g. MoveAsync), awaiting here
    ///     would deadlock the two grains' mailboxes on each other. Fire it and let the map's
    ///     publish return immediately; delivery still lands on the player's own mailbox.
    /// </summary>
    private sealed class AoiObserver(GrainActivation shell, PlayerRuntime runtime) : IAsyncObserver<DeltaBatch>
    {
        public ValueTask OnNextAsync(DeltaBatch item, StreamSequenceToken? token = null)
        {
            _ = DeliverAsync(item);
            return ValueTask.CompletedTask;
        }

        private async Task DeliverAsync(DeltaBatch item)
        {
            try
            {
                await shell.PostAsync((runtime, item), static ctx =>
                {
                    ctx.runtime.ReceivedDeltaCount += ctx.item.Deltas.Length;
                    return ValueTask.CompletedTask;
                });
            }
            catch
            {
                // Best-effort AoI delivery — e.g. the player deactivated between publish and delivery.
            }
        }

        public ValueTask OnErrorAsync(Exception ex) => ValueTask.CompletedTask;
        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
    }
}
