using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.PingPong;

internal readonly struct PingPongBehavior_PingInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IPingPongGrain)behavior).PingAsync();
    public void Serialize(ref CodecWriter writer) { }
}
