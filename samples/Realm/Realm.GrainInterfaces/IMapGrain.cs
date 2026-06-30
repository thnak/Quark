using Quark.Core.Abstractions.Grains;
using Realm.Common.Dtos;

namespace Realm.GrainInterfaces;

public interface IMapGrain : IGrainWithStringKey
{
    Task<EnterResult> EnterAsync(string entityId, Coord at, EntityKind kind);
    Task LeaveAsync(string entityId);
    Task<MoveResult> TryMoveAsync(string entityId, Direction dir);
    Task<MapSnapshot> SnapshotAsync();
}
