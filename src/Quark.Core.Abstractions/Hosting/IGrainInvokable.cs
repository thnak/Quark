using Quark.Core.Abstractions.Grains;

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

    /// <summary>Invokes the grain method on <paramref name="grain" /> and returns the result.</summary>
    ValueTask<TResult> Invoke(Grain grain);
}

/// <summary>
///     Variant of <see cref="IGrainInvokable{TResult}" /> for grain methods that return
///     <see cref="System.Threading.Tasks.Task" /> or <see cref="System.Threading.Tasks.ValueTask" />.
/// </summary>
public interface IGrainVoidInvokable
{
    /// <summary>Stable numeric method identifier used for tracing and fault injection.</summary>
    uint MethodId { get; }

    /// <summary>Invokes the grain method on <paramref name="grain" />.</summary>
    ValueTask Invoke(Grain grain);
}

/// <summary>
///     Carries the arguments for one observer method call and knows how to invoke it on a local
///     observer object (which is <em>not</em> a <see cref="Grain" /> subclass).
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
}
