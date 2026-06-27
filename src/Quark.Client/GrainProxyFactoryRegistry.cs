using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
///     Holds per-interface proxy factory delegates.
///     Populated at startup via <c>AddGrainProxy&lt;TInterface, TProxy&gt;()</c>.
/// </summary>
public sealed class GrainProxyFactoryRegistry
{
    private readonly Dictionary<Type, Func<GrainId, IGrainCallInvoker, IGrain>> _factories = new();

    /// <summary>
    ///     Registers a factory that creates <typeparamref name="TProxy" /> for
    ///     <typeparamref name="TInterface" />.
    /// </summary>
    public void Register<TInterface, TProxy>(Func<GrainId, IGrainCallInvoker, TProxy> factory)
        where TInterface : IGrain
        where TProxy : class, TInterface
    {
        // Wrap as IGrain-returning delegate — avoids DynamicInvoke in the runtime-typed overload.
        _factories[typeof(TInterface)] = factory;
    }

    /// <summary>
    ///     Creates a proxy for <typeparamref name="TInterface" /> with the given identity and invoker.
    /// </summary>
    public TInterface CreateProxy<TInterface>(GrainId grainId, IGrainCallInvoker invoker)
        where TInterface : IGrain
    {
        if (_factories.TryGetValue(typeof(TInterface), out Func<GrainId, IGrainCallInvoker, IGrain>? factory))
        {
            return (TInterface)factory(grainId, invoker);
        }

        throw new InvalidOperationException(
            $"No proxy factory registered for grain interface '{typeof(TInterface).FullName}'. " +
            "Call services.AddGrainProxy<TInterface, TProxy>() during startup.");
    }

    /// <summary>
    ///     Creates a proxy for the interface type provided as a <see cref="Type" /> parameter.
    /// </summary>
    public IGrain CreateProxy(Type interfaceType, GrainId grainId, IGrainCallInvoker invoker)
    {
        if (_factories.TryGetValue(interfaceType, out Func<GrainId, IGrainCallInvoker, IGrain>? factory))
        {
            return factory(grainId, invoker);
        }

        throw new InvalidOperationException(
            $"No proxy factory registered for grain interface '{interfaceType.FullName}'. " +
            "Call services.AddGrainProxy<TInterface, TProxy>() during startup.");
    }
}
