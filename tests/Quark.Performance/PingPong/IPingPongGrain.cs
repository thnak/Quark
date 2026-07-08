using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

public interface IPingPongGrain : IGrainWithStringKey
{
    ValueTask PingAsync();
}
