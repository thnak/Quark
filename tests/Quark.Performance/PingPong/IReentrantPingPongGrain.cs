using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

public interface IReentrantPingPongGrain : IGrainWithStringKey, IPingable
{
}
