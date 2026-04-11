namespace Quark.Core.Abstractions;

/// <summary>
/// Dispatches method calls to a grain instance by method ID.
/// One implementation is generated per grain class; registered in DI at startup.
/// </summary>
public interface IGrainMethodInvoker
{
    /// <summary>
    /// Invokes the grain method identified by <paramref name="methodId"/> with the supplied
    /// <paramref name="arguments"/>.
    /// </summary>
    /// <param name="grain">The target grain instance.</param>
    /// <param name="methodId">Stable numeric method identifier (assigned by the codegen).</param>
    /// <param name="arguments">Boxed method arguments, or <c>null</c> for no-arg methods.</param>
    /// <returns>The boxed return value, or <c>null</c> for void-like methods.</returns>
    ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments);
}

/// <summary>
/// Registry that maps grain implementation CLR types to their <see cref="IGrainMethodInvoker"/>.
/// Populated at startup via <c>AddGrainMethodInvoker&lt;TGrain, TInvoker&gt;()</c>.
/// </summary>
public interface IGrainMethodInvokerRegistry
{
    /// <summary>
    /// Returns the method invoker for <paramref name="grainType"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">When no invoker is registered.</exception>
    IGrainMethodInvoker GetInvoker(Type grainType);
}
