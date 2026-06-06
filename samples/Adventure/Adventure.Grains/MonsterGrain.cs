using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Timers;

namespace Adventure.Grains;

public class MonsterGrain : Grain, IMonsterGrain
{
    private MonsterInfo? _info;
    private IRoomGrain? _room;
    private IGrainTimer? _timer;

    public async Task SetInfoAsync(MonsterInfo info, long startRoomId)
    {
        _info = info;
        _room = GrainFactory.GetGrain<IRoomGrain>(startRoomId);
        await _room.EnterMonster(info);

        // Same call site as Orleans — works via the new stateless overload added in Part 1.
        _timer = RegisterGrainTimer(
            _ => Move(),
            TimeSpan.FromSeconds(150),
            TimeSpan.FromSeconds(150));
    }

    public Task<MonsterInfo?> GetInfoAsync() => Task.FromResult(_info);

    public async Task KillAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_room is not null)
        {
            await _room.ExitMonster(GetPrimaryKeyLong());
            _room = null;
        }
    }

    private async Task Move()
    {
        if (_room is null || _info is null) return;

        var info = await _room.GetInfoAsync();
        if (info is null) return;

        long[] exits = [info.North, info.South, info.East, info.West];
        var valid = exits.Where(e => e != 0).ToArray();
        if (valid.Length == 0) return;

        var newRoomId = valid[Random.Shared.Next(valid.Length)];
        await _room.ExitMonster(GetPrimaryKeyLong());
        _room = GrainFactory.GetGrain<IRoomGrain>(newRoomId);
        await _room.EnterMonster(_info);
    }
}
