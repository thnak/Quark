using Quark.Core.Abstractions.Grains;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Carries the arguments for one observer method call and knows how to invoke it on a local
///     observer object (which is <em>not</em> an <see cref="IGrainBehavior" />).
///     One struct is generated per observer method; the observer proxy instantiates it with the
///     typed arguments and passes it to
///     <see cref="IGrainCallInvoker.InvokeObserverAsync{TInvokable}" />.
/// </summary>
public interface IObserverVoidInvokable
{
    /// <summary>Stable numeric method identifier used for tracing.</summary>
    uint MethodId { get; }

    /// <summary>Invokes the observer method on <paramref name="target" />.</summary>
    ValueTask Invoke(object target);

    /// <summary>Serialises all method arguments into <paramref name="writer" /> for TCP transport.</summary>
    void Serialize(ref CodecWriter writer);
}