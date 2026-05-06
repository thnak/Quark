using Quark.Core.Abstractions;
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
/// Default implementation of <see cref="IGrainMethodInvokerRegistry"/>.
/// Populated at startup via <c>AddGrainMethodInvoker&lt;TGrain, TInvoker&gt;()</c>.
/// </summary>
public sealed class GrainMethodInvokerRegistry : IGrainMethodInvokerRegistry
{
    private readonly ConcurrentDictionary<Type, IGrainMethodInvoker> _invokers = new();

    /// <summary>
    /// Registers <paramref name="invoker"/> for grain type <paramref name="grainType"/>.
    /// </summary>
    public void Register(Type grainType, IGrainMethodInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(grainType);
        ArgumentNullException.ThrowIfNull(invoker);
        _invokers[grainType] = invoker;
    }

    /// <inheritdoc/>
    public IGrainMethodInvoker GetInvoker(Type grainType)
    {
        if (_invokers.TryGetValue(grainType, out var invoker))
            return invoker;

        throw new InvalidOperationException(
            $"No IGrainMethodInvoker registered for grain type '{grainType.FullName}'. " +
            "Call services.AddGrainMethodInvoker<TGrain, TInvoker>() during startup.");
    }
}
