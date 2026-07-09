using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.PingPong;

internal readonly struct ReentrantPingPongBehavior_PingInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IReentrantPingPongGrain)behavior).PingAsync();
    public void Serialize(ref CodecWriter writer) { }
}
