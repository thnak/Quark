using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.ActorLifecycle;

internal readonly struct ActorLifecycleBehavior_PingInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IActorLifecycleGrain)behavior).PingAsync();
    public void Serialize(ref CodecWriter writer) { }
}
