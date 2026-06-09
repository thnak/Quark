using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Tests.Unit.Integration;

internal readonly struct ManagedBuffer_GetDataInvokable : IGrainInvokable<string>
{
    public uint MethodId => 1u;
    public ValueTask<string> Invoke(IGrainBehavior behavior) => new(((IManagedBufferGrain)behavior).GetDataAsync());
    public void Serialize(ref CodecWriter writer) { }
    public string DeserializeResult(ref CodecReader reader) => reader.ReadString();
}