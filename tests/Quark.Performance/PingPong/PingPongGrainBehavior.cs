using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

public sealed class PingPongGrainBehavior : IGrainBehavior, IPingPongGrain
{
    public ValueTask PingAsync() => default;
}
