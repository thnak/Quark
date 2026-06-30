using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;
using Quark.Persistence.Abstractions;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.GrainInterfaces;

namespace Realm.Grains;

[PreferLocalPlacement]
public sealed class PlayerBehavior : IGrainBehavior, IPlayerGrain, IActivationLifecycle
{
    private readonly IPersistentActivationMemory<PlayerState> _memory;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;

    public PlayerBehavior(
        IPersistentActivationMemory<PlayerState> memory,
        IGrainFactory factory,
        ICallContext ctx)
    {
        _memory = memory;
        _factory = factory;
        _ctx = ctx;
    }

    private PlayerState S => _memory.Value;
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
            // Phase 3: cross-map transition — leave current map, enter new map, swap AoI subscriptions.
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

        await _memory.SaveAsync();
    }
}
