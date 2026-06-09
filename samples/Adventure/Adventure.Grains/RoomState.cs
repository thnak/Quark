using Adventure.GrainInterfaces;

namespace Adventure.Grains;

public sealed class RoomState
{
    public RoomInfo? Info { get; set; }
    public List<PlayerInfo> Players { get; } = [];
    public List<Thing> Things { get; } = [];
    public List<MonsterInfo> Monsters { get; } = [];
}