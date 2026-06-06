using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;

namespace Adventure.Grains;

public class PlayerGrain : Grain, IPlayerGrain
{
    private string? _name;
    private IRoomGrain? _room;
    private readonly List<Thing> _inventory = new();

    public async Task SetInfoAsync(string name)
    {
        _name = name;
        _room = GrainFactory.GetGrain<IRoomGrain>(1L);
        await _room.Enter(Me());
    }

    public async Task<string> DescribeAsync()
    {
        if (_room is null) return "You are nowhere.";
        var desc = await _room.DescribeAsync(Me());
        return _inventory.Count == 0
            ? desc + "\nYou carry nothing."
            : desc + $"\nCarrying: {string.Join(", ", _inventory.Select(t => t.Name))}";
    }

    public async Task<string> GoAsync(string direction)
    {
        if (_room is null) return "You are nowhere.";
        var info = await _room.GetInfoAsync();
        if (info is null) return "You are lost.";

        long nextId = direction.Trim().ToLowerInvariant() switch
        {
            "north" => info.North,
            "south" => info.South,
            "east"  => info.East,
            "west"  => info.West,
            _       => 0
        };
        if (nextId == 0) return "You can't go that way.";

        await _room.Exit(Me());
        _room = GrainFactory.GetGrain<IRoomGrain>(nextId);
        await _room.Enter(Me());
        return await _room.DescribeAsync(Me());
    }

    public async Task<string> PickUpAsync(string thingName)
    {
        if (_room is null) return "You are nowhere.";
        var thing = await _room.PickUpAsync(Me(), thingName);
        if (thing is null) return $"There is no '{thingName}' here.";
        if (!thing.CanCarry) return $"You can't pick up the {thing.Name}.";
        _inventory.Add(thing);
        return $"You pick up the {thing.Name}.";
    }

    public async Task<string> DropAsync(string thingName)
    {
        var thing = _inventory.FirstOrDefault(
            t => string.Equals(t.Name, thingName, StringComparison.OrdinalIgnoreCase));
        if (thing is null) return $"You don't have a '{thingName}'.";
        _inventory.Remove(thing);
        if (_room is not null)
            await _room.DropAsync(Me(), thing);
        return $"You drop the {thing.Name}.";
    }

    public async Task<string> KillAsync(string monsterName)
    {
        if (_room is null) return "You are nowhere.";
        var info = await _room.FindMonsterAsync(monsterName);
        if (info is null) return $"There is no '{monsterName}' here.";
        var monster = GrainFactory.GetGrain<IMonsterGrain>(info.Id);
        await monster.KillAsync();
        return $"You killed the {monsterName}!";
    }

    public Task<string> GetInventoryAsync()
    {
        return Task.FromResult(_inventory.Count == 0
            ? "You carry nothing."
            : $"Carrying: {string.Join(", ", _inventory.Select(t => t.Name))}");
    }

    public async Task Die()
    {
        if (_room is not null)
            await _room.Exit(Me());
        _inventory.Clear();
        _room = GrainFactory.GetGrain<IRoomGrain>(1L);
        await _room.Enter(Me());
    }

    private PlayerInfo Me() => new() { Key = GetPrimaryKey(), Name = _name };
}
