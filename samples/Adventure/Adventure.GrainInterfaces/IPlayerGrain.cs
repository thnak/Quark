using Quark.Core.Abstractions.Grains;

namespace Adventure.GrainInterfaces;

public interface IPlayerGrain : IGrainWithGuidKey
{
    Task SetInfoAsync(string name);
    Task<string> DescribeAsync();
    Task<string> GoAsync(string direction);
    Task<string> PickUpAsync(string thingName);
    Task<string> DropAsync(string thingName);
    Task<string> KillAsync(string monsterName);
    Task<string> GetInventoryAsync();
    Task Die();
}
