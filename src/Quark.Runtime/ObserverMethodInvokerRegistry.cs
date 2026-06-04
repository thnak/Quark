using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Maps observer interface types to their <see cref="IObserverMethodInvoker" /> implementations.
///     Populated at startup via <c>AddObserverMethodInvoker&lt;TObserver, TInvoker&gt;()</c>.
/// </summary>
public sealed class ObserverMethodInvokerRegistry
{
    private readonly ConcurrentDictionary<Type, IObserverMethodInvoker> _invokers = new();

    /// <summary>Returns the invoker registered for <paramref name="observerType" />.</summary>
    public IObserverMethodInvoker GetInvoker(Type observerType)
    {
        if (_invokers.TryGetValue(observerType, out IObserverMethodInvoker? invoker))
        {
            return invoker;
        }

        throw new InvalidOperationException(
            $"No IObserverMethodInvoker registered for observer type '{observerType.FullName}'. " +
            "Call services.AddObserverMethodInvoker<TObserver, TInvoker>() during startup.");
    }

    /// <summary>Registers <paramref name="invoker" /> for observer type <paramref name="observerType" />.</summary>
    public void Register(Type observerType, IObserverMethodInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(observerType);
        ArgumentNullException.ThrowIfNull(invoker);
        _invokers[observerType] = invoker;
    }
}
