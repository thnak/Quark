using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Adventure.Grains;

public sealed class PlayerBehavior : IGrainBehavior, IPlayerGrain
{
    private readonly IActivationMemory<PlayerState> _memory;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;

    public PlayerBehavior(IActivationMemory<PlayerState> memory, IGrainFactory factory, ICallContext ctx)
    {
        _memory = memory;
        _factory = factory;
        _ctx = ctx;
    }

    private PlayerState S => _memory.Value;

    public async Task SetInfoAsync(string name)
    {
        S.Name = name;
        S.Room = _factory.GetGrain<IRoomGrain>(1L);
        await S.Room.Enter(Me());
    }

    public async Task<string> DescribeAsync()
    {
        if (S.Room is null) return "You are nowhere.";
        var desc = await S.Room.DescribeAsync(Me());
        return S.Inventory.Count == 0
            ? desc + "\nYou carry nothing."
            : desc + $"\nCarrying: {string.Join(", ", S.Inventory.Select(t => t.Name))}";
    }

    public async Task<string> GoAsync(string direction)
    {
        if (S.Room is null) return "You are nowhere.";
        var info = await S.Room.GetInfoAsync();
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

        await S.Room.Exit(Me());
        S.Room = _factory.GetGrain<IRoomGrain>(nextId);
        await S.Room.Enter(Me());
        return await S.Room.DescribeAsync(Me());
    }

    public async Task<string> PickUpAsync(string thingName)
    {
        if (S.Room is null) return "You are nowhere.";
        var thing = await S.Room.PickUpAsync(Me(), thingName);
        if (thing is null) return $"There is no '{thingName}' here.";
        if (!thing.CanCarry) return $"You can't pick up the {thing.Name}.";
        S.Inventory.Add(thing);
        return $"You pick up the {thing.Name}.";
    }

    public async Task<string> DropAsync(string thingName)
    {
        var thing = S.Inventory.FirstOrDefault(
            t => string.Equals(t.Name, thingName, StringComparison.OrdinalIgnoreCase));
        if (thing is null) return $"You don't have a '{thingName}'.";
        S.Inventory.Remove(thing);
        if (S.Room is not null)
            await S.Room.DropAsync(Me(), thing);
        return $"You drop the {thing.Name}.";
    }

    public async Task<string> KillAsync(string monsterName)
    {
        if (S.Room is null) return "You are nowhere.";
        var info = await S.Room.FindMonsterAsync(monsterName);
        if (info is null) return $"There is no '{monsterName}' here.";
        var monster = _factory.GetGrain<IMonsterGrain>(info.Id);
        await monster.KillAsync();
        return $"You killed the {monsterName}!";
    }

    public Task<string> GetInventoryAsync()
    {
        return Task.FromResult(S.Inventory.Count == 0
            ? "You carry nothing."
            : $"Carrying: {string.Join(", ", S.Inventory.Select(t => t.Name))}");
    }

    public async Task Die()
    {
        if (S.Room is not null)
            await S.Room.Exit(Me());
        S.Inventory.Clear();
        S.Room = _factory.GetGrain<IRoomGrain>(1L);
        await S.Room.Enter(Me());
    }

    private PlayerInfo Me() => new() { Key = Guid.ParseExact(_ctx.GrainId.Key, "N"), Name = S.Name };
}
