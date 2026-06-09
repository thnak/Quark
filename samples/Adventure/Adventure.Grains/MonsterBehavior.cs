using System.Globalization;
using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;

namespace Adventure.Grains;

public sealed class MonsterBehavior : IGrainBehavior, IMonsterGrain
{
    private readonly IActivationMemory<MonsterState> _memory;
    private readonly IGrainFactory _factory;
    private readonly IActivationShellAccessor _shell;
    private readonly ICallContext _ctx;

    public MonsterBehavior(
        IActivationMemory<MonsterState> memory,
        IGrainFactory factory,
        IActivationShellAccessor shell,
        ICallContext ctx)
    {
        _memory = memory;
        _factory = factory;
        _shell = shell;
        _ctx = ctx;
    }

    private MonsterState S => _memory.Value;

    public async Task SetInfoAsync(MonsterInfo info, long startRoomId)
    {
        if (S.Id == 0)
            S.Id = long.Parse(_ctx.GrainId.Key, CultureInfo.InvariantCulture);
        S.Factory ??= _factory;
        S.Info = info;
        S.Room = _factory.GetGrain<IRoomGrain>(startRoomId);
        await S.Room.EnterMonster(info);

        S.Timer?.Dispose();
        S.Timer = _shell.Shell.RegisterTimer<MonsterState>(
            static (state, ct) => MoveAsync(state),
            S,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(150),
                Period = TimeSpan.FromSeconds(150)
            });
    }

    public Task<MonsterInfo?> GetInfoAsync() => Task.FromResult(S.Info);

    public async Task KillAsync()
    {
        S.Timer?.Dispose();
        S.Timer = null;
        if (S.Room is not null)
        {
            await S.Room.ExitMonster(S.Id);
            S.Room = null;
        }
    }

    private static async Task MoveAsync(MonsterState state)
    {
        if (state.Room is null || state.Info is null || state.Factory is null) return;

        var info = await state.Room.GetInfoAsync();
        if (info is null) return;

        long[] exits = [info.North, info.South, info.East, info.West];
        var valid = exits.Where(e => e != 0).ToArray();
        if (valid.Length == 0) return;

        var newRoomId = valid[Random.Shared.Next(valid.Length)];
        await state.Room.ExitMonster(state.Id);
        state.Room = state.Factory.GetGrain<IRoomGrain>(newRoomId);
        await state.Room.EnterMonster(state.Info);
    }
}
