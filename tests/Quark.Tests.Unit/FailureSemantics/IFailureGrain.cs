using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.FailureSemantics;

public interface IFailureGrain : IGrainWithStringKey
{
    Task SetAsync(int value);
    Task<int> GetAsync();
    Task ThrowAsync(string message);
    Task SetThenThrowAsync(int value);
}
