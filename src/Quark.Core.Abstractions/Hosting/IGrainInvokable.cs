using Quark.Core.Abstractions.Grains;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Carries the arguments for one grain method call and knows how to invoke it.
///     One struct is generated per grain method; the proxy instantiates it with
///     the typed arguments and passes it to <see cref="IGrainCallInvoker" />.
///     No boxing, no argument array — all types are visible to the AOT linker.
/// </summary>
public interface IGrainInvokable<TResult>
{
    /// <summary>Stable numeric method identifier used for tracing and fault injection.</summary>
    uint MethodId { get; }

    /// <summary>Invokes the grain method on <paramref name="behavior" /> and returns the result.</summary>
    ValueTask<TResult> Invoke(IGrainBehavior behavior);

    /// <summary>Serialises all method arguments into <paramref name="writer" /> for transport.</summary>
    void Serialize(ref CodecWriter writer);

    /// <summary>Deserialises the return value from the transport response payload.</summary>
    TResult DeserializeResult(ref CodecReader reader);
}