using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct ManagedBuffer_SelfDestructInvokable : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).SelfDestructAsync());
    public void Serialize(ref CodecWriter writer) { }
}
