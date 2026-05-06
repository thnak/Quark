using Quark.Core.Abstractions;

namespace Quark.Tests.Unit.Integration;

public interface ICounterGrain : IGrainWithStringKey
{
    Task<long> IncrementAsync();
    Task<long> GetValueAsync();
    Task ResetAsync();
}