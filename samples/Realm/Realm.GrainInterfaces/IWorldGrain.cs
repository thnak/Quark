using Quark.Core.Abstractions.Grains;
using Realm.Common.Dtos;

namespace Realm.GrainInterfaces;

public interface IWorldGrain : IGrainWithStringKey
{
    Task<PlayerSpawn> LoginAsync(string playerId);
    Task<MapDescriptor> GetMapAsync(string mapId);
}
