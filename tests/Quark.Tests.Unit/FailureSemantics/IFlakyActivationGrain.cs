using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.FailureSemantics;

public interface IFlakyActivationGrain : IGrainWithStringKey
{
    Task<int> PingAsync();
}
