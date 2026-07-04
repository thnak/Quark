using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
///     Holds per-observer-interface proxy factory delegates.
///     Populated at startup via <c>AddObserverProxy&lt;TInterface, TProxy&gt;()</c>.
/// </summary>
public sealed class ObserverProxyFactoryRegistry
{
    private readonly Dictionary<Type, Func<GrainId, IGrainCallInvoker, IGrainObserver>> _factories = new();

    /// <summary>Registers a factory that creates <typeparamref name="TProxy" /> for <typeparamref name="TInterface" />.</summary>
    public void Register<TInterface, TProxy>(Func<GrainId, IGrainCallInvoker, TProxy> factory)
        where TInterface : IGrainObserver
        where TProxy : class, TInterface
    {
        _factories[typeof(TInterface)] = factory;
    }

    /// <summary>Creates a proxy for <typeparamref name="TInterface" /> with the given identity and invoker.</summary>
    public TInterface CreateProxy<TInterface>(GrainId grainId, IGrainCallInvoker invoker)
        where TInterface : IGrainObserver
    {
        if (_factories.TryGetValue(typeof(TInterface), out Func<GrainId, IGrainCallInvoker, IGrainObserver>? factory))
        {
            return (TInterface)factory(grainId, invoker);
        }

        throw new InvalidOperationException(
            $"No proxy factory registered for observer interface '{typeof(TInterface).FullName}'. " +
            "Call services.AddObserverProxy<TInterface, TProxy>() during startup.");
    }
}
