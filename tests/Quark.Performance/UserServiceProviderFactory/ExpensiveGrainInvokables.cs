using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.UserServiceProviderFactory;

internal readonly struct ExpensiveGrain_GetConnectionCountInvokable : IGrainInvokable<int>
{
    public uint MethodId => 0u;
    public ValueTask<int> Invoke(IGrainBehavior behavior) => ((IExpensiveGrain)behavior).GetConnectionCountAsync();
    public void Serialize(ref CodecWriter writer) { }
    public int DeserializeResult(ref CodecReader reader) => reader.ReadInt32();
}
