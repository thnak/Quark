using Quark.Core.Abstractions.Grains;

namespace Adventure.GrainInterfaces;

public interface IMonsterGrain : IGrainWithIntegerKey
{
    Task SetInfoAsync(MonsterInfo info, long startRoomId);
    Task<MonsterInfo?> GetInfoAsync();
    Task KillAsync();
}
