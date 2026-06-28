using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.Integration;

public interface IEagerResourceGrain : IGrainWithStringKey
{
    Task<string> GetLoadedByIdAsync();
    Task<int> GetInitCountAsync();
    Task<bool> WasValueAvailableInOnActivateAsync();
    Task SelfDestructAsync();
}
