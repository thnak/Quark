using System.Text;
using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Adventure.Grains;

public sealed class RoomBehavior : IGrainBehavior, IRoomGrain
{
    private readonly IActivationMemory<RoomState> _memory;

    public RoomBehavior(IActivationMemory<RoomState> memory) => _memory = memory;

    private RoomState S => _memory.Value;

    public Task SetInfoAsync(RoomInfo info)
    {
        S.Info = info;
        return Task.CompletedTask;
    }

    public Task AddThingAsync(string name, string category, bool canCarry)
    {
        S.Things.Add(new Thing { Name = name, Category = category, CanCarry = canCarry });
        return Task.CompletedTask;
    }

    public Task<RoomInfo?> GetInfoAsync() => Task.FromResult(S.Info);

    public Task<string> DescribeAsync(PlayerInfo player)
    {
        if (S.Info is null) return Task.FromResult("An empty void.");

        var sb = new StringBuilder();
        sb.AppendLine(S.Info.Name);
        sb.AppendLine(S.Info.Description);

        var exits = new List<string>(4);
        if (S.Info.North != 0) exits.Add("north");
        if (S.Info.South != 0) exits.Add("south");
        if (S.Info.East != 0)  exits.Add("east");
        if (S.Info.West != 0)  exits.Add("west");
        if (exits.Count > 0)
            sb.AppendLine($"Exits: {string.Join(", ", exits)}");

        if (S.Things.Count > 0)
            sb.AppendLine($"You see: {string.Join(", ", S.Things.Select(t => t.Name))}");

        var others = S.Players
            .Where(p => p.Key != player.Key)
            .Select(p => p.Name ?? "someone")
            .ToList();
        if (others.Count > 0)
            sb.AppendLine($"Also here: {string.Join(", ", others)}");

        if (S.Monsters.Count > 0)
            sb.AppendLine($"Monsters: {string.Join(", ", S.Monsters.Select(m => m.Name))}");

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public Task Enter(PlayerInfo player)
    {
        if (!S.Players.Any(p => p.Key == player.Key))
            S.Players.Add(player);
        return Task.CompletedTask;
    }

    public Task Exit(PlayerInfo player)
    {
        S.Players.RemoveAll(p => p.Key == player.Key);
        return Task.CompletedTask;
    }

    public Task EnterMonster(MonsterInfo info)
    {
        if (!S.Monsters.Any(m => m.Id == info.Id))
            S.Monsters.Add(info);
        return Task.CompletedTask;
    }

    public Task ExitMonster(long monsterId)
    {
        S.Monsters.RemoveAll(m => m.Id == monsterId);
        return Task.CompletedTask;
    }

    public Task<Thing?> PickUpAsync(PlayerInfo player, string thingName)
    {
        var thing = S.Things.FirstOrDefault(
            t => string.Equals(t.Name, thingName, StringComparison.OrdinalIgnoreCase));
        if (thing is null) return Task.FromResult<Thing?>(null);
        if (!thing.CanCarry) return Task.FromResult<Thing?>(thing);
        S.Things.Remove(thing);
        return Task.FromResult<Thing?>(thing);
    }

    public Task DropAsync(PlayerInfo player, Thing thing)
    {
        S.Things.Add(thing);
        return Task.CompletedTask;
    }

    public Task<MonsterInfo?> FindMonsterAsync(string name)
    {
        return Task.FromResult(
            S.Monsters.FirstOrDefault(
                m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)));
    }
}
