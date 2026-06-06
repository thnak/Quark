using Quark.Core.Abstractions.Grains;

namespace Adventure.GrainInterfaces;

public interface IRoomGrain : IGrainWithIntegerKey
{
    Task SetInfoAsync(RoomInfo info);
    Task AddThingAsync(string name, string category, bool canCarry);
    Task<RoomInfo?> GetInfoAsync();
    Task<string> DescribeAsync(PlayerInfo player);
    Task Enter(PlayerInfo player);
    Task Exit(PlayerInfo player);
    Task EnterMonster(MonsterInfo info);
    Task ExitMonster(long monsterId);
    Task<Thing?> PickUpAsync(PlayerInfo player, string thingName);
    Task DropAsync(PlayerInfo player, Thing thing);
    Task<MonsterInfo?> FindMonsterAsync(string name);
}
