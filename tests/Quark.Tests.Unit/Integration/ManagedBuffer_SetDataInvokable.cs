using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct ManagedBuffer_SetDataInvokable(string value) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).SetDataAsync(value));
    public void Serialize(ref CodecWriter writer) => writer.WriteString(value);
}
