using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct CounterBehavior_ResetInvokable : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((ICounterGrain)behavior).ResetAsync());
    public void Serialize(ref CodecWriter writer) { }
}
