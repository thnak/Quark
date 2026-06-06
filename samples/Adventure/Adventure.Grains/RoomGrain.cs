using System.Text;
using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;

namespace Adventure.Grains;

public class RoomGrain : Grain, IRoomGrain
{
    private RoomInfo? _info;
    private readonly List<PlayerInfo> _players = new();
    private readonly List<Thing> _things = new();
    private readonly List<MonsterInfo> _monsters = new();

    public Task SetInfoAsync(RoomInfo info)
    {
        _info = info;
        return Task.CompletedTask;
    }

    public Task AddThingAsync(string name, string category, bool canCarry)
    {
        _things.Add(new Thing { Name = name, Category = category, CanCarry = canCarry });
        return Task.CompletedTask;
    }

    public Task<RoomInfo?> GetInfoAsync() => Task.FromResult(_info);

    public Task<string> DescribeAsync(PlayerInfo player)
    {
        if (_info is null) return Task.FromResult("An empty void.");

        var sb = new StringBuilder();
        sb.AppendLine(_info.Name);
        sb.AppendLine(_info.Description);

        var exits = new List<string>(4);
        if (_info.North != 0) exits.Add("north");
        if (_info.South != 0) exits.Add("south");
        if (_info.East != 0)  exits.Add("east");
        if (_info.West != 0)  exits.Add("west");
        if (exits.Count > 0)
            sb.AppendLine($"Exits: {string.Join(", ", exits)}");

        if (_things.Count > 0)
            sb.AppendLine($"You see: {string.Join(", ", _things.Select(t => t.Name))}");

        var others = _players
            .Where(p => p.Key != player.Key)
            .Select(p => p.Name ?? "someone")
            .ToList();
        if (others.Count > 0)
            sb.AppendLine($"Also here: {string.Join(", ", others)}");

        if (_monsters.Count > 0)
            sb.AppendLine($"Monsters: {string.Join(", ", _monsters.Select(m => m.Name))}");

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    public Task Enter(PlayerInfo player)
    {
        if (!_players.Any(p => p.Key == player.Key))
            _players.Add(player);
        return Task.CompletedTask;
    }

    public Task Exit(PlayerInfo player)
    {
        _players.RemoveAll(p => p.Key == player.Key);
        return Task.CompletedTask;
    }

    public Task EnterMonster(MonsterInfo info)
    {
        if (!_monsters.Any(m => m.Id == info.Id))
            _monsters.Add(info);
        return Task.CompletedTask;
    }

    public Task ExitMonster(long monsterId)
    {
        _monsters.RemoveAll(m => m.Id == monsterId);
        return Task.CompletedTask;
    }

    public Task<Thing?> PickUpAsync(PlayerInfo player, string thingName)
    {
        var thing = _things.FirstOrDefault(
            t => string.Equals(t.Name, thingName, StringComparison.OrdinalIgnoreCase));
        if (thing is null || !thing.CanCarry)
            return Task.FromResult<Thing?>(null);
        _things.Remove(thing);
        return Task.FromResult<Thing?>(thing);
    }

    public Task DropAsync(PlayerInfo player, Thing thing)
    {
        _things.Add(thing);
        return Task.CompletedTask;
    }

    public Task<MonsterInfo?> FindMonsterAsync(string name)
    {
        return Task.FromResult(
            _monsters.FirstOrDefault(
                m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)));
    }
}
