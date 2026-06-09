using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.Integration;

public interface IManagedBufferGrain : IGrainWithStringKey
{
    Task<long> GetInitCountAsync();
    Task<string> GetDataAsync();
    Task SetDataAsync(string value);
    Task SelfDestructAsync();
}