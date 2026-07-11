using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.UserServiceProviderFactory;

public interface IExpensiveGrain : IGrainWithStringKey
{
    ValueTask<int> GetConnectionCountAsync();
}
