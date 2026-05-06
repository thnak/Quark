namespace Quark.Core.Abstractions;

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