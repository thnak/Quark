using Quark.Core.Abstractions.Grains;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Variant of <see cref="IGrainInvokable{TResult}" /> for grain methods that return
///     <see cref="System.Threading.Tasks.Task" /> or <see cref="System.Threading.Tasks.ValueTask" />.
/// </summary>
public interface IGrainVoidInvokable
{
    /// <summary>Stable numeric method identifier used for tracing and fault injection.</summary>
    uint MethodId { get; }

    /// <summary>Invokes the grain method on <paramref name="behavior" />.</summary>
    ValueTask Invoke(IGrainBehavior behavior);

    /// <summary>Serialises all method arguments into <paramref name="writer" /> for transport.</summary>
    void Serialize(ref CodecWriter writer);
}