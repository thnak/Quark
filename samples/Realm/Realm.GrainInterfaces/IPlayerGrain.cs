using Quark.Core.Abstractions.Grains;
using Realm.Common.Dtos;

namespace Realm.GrainInterfaces;

public interface IPlayerGrain : IGrainWithStringKey
{
    Task LoginAsync();
    Task MoveAsync(Direction dir);
    Task LogoutAsync();
    Task<AoiStatus> GetAoiStatusAsync();
}
