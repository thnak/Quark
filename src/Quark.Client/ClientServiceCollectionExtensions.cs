using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Client;

/// <summary>
///     Dependency-injection extension methods for the Quark client.
/// </summary>
public static class ClientServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-process cluster client and its dependencies.
    ///     The resulting <see cref="IClusterClient" /> routes calls directly to the local silo
    ///     (cohosted scenario, equivalent to running client and silo in the same process).
    /// </summary>
    public static IServiceCollection AddLocalClusterClient(this IServiceCollection services)
    {
        services.TryAddSingleton<GrainProxyFactoryRegistry>();
        services.TryAddSingleton<GrainInterfaceTypeRegistry>();
        services.TryAddSingleton<ObserverProxyFactoryRegistry>();
        services.TryAddSingleton<LocalGrainFactory>(sp => new LocalGrainFactory(
            sp.GetRequiredService<GrainProxyFactoryRegistry>(),
            sp.GetRequiredService<GrainInterfaceTypeRegistry>(),
            sp.GetRequiredService<IGrainCallInvoker>(),
            sp.GetService<ObserverProxyFactoryRegistry>(),
            sp.GetService<ObserverRegistry>()));
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<LocalGrainFactory>());
        services.TryAddSingleton<LocalClusterClient>();
        services.TryAddSingleton<IClusterClient>(sp => sp.GetRequiredService<LocalClusterClient>());
        services.AddHostedService<ClientStartupService>();
        return services;
    }

    /// <summary>
    ///     Registers a grain proxy factory so that <see cref="IClusterClient.GetGrain{T}" /> can
    ///     create a proxy for <typeparamref name="TInterface" />.
    /// </summary>
    /// <typeparam name="TInterface">The grain interface (e.g. <c>ICounterGrain</c>).</typeparam>
    /// <typeparam name="TProxy">
    ///     The generated proxy class that implements <typeparamref name="TInterface" />
    ///     and routes calls through <see cref="IGrainCallInvoker" />.
    /// </typeparam>
    /// <param name="grainTypeName">
    ///     Optional override for the grain-type name (defaults to the implementation class name
    ///     without the leading "I", e.g. <c>CounterGrain</c>).
    /// </param>
    public static IServiceCollection AddGrainProxy<TInterface, TProxy>(
        this IServiceCollection services,
        string? grainTypeName = null)
        where TInterface : IGrain
        where TProxy : class, TInterface, IGrainProxyActivator<TProxy>
    {
        services.AddSingleton<IProxyRegistration>(
            new ProxyRegistration<TInterface, TProxy>(grainTypeName));
        return services;
    }

    /// <summary>
    ///     Registers an observer proxy factory so that
    ///     <see cref="IGrainFactory.CreateObjectReference{TGrainObserver}" /> can wrap
    ///     a local <typeparamref name="TInterface" /> object in a proxy.
    /// </summary>
    public static IServiceCollection AddObserverProxy<TInterface, TProxy>(
        this IServiceCollection services)
        where TInterface : IGrainObserver
        where TProxy : class, TInterface, IGrainObserverProxyActivator<TProxy>
    {
        services.AddSingleton<IObserverProxyRegistration>(
            new ObserverProxyRegistration<TInterface, TProxy>());
        return services;
    }

    // -----------------------------------------------------------------------

    internal interface IProxyRegistration
    {
        void Apply(GrainProxyFactoryRegistry proxyRegistry, GrainInterfaceTypeRegistry interfaceRegistry);
    }

    internal interface IObserverProxyRegistration
    {
        void Apply(ObserverProxyFactoryRegistry observerProxyRegistry);
    }

    private sealed class ProxyRegistration<TInterface, TProxy>(string? grainTypeName)
        : IProxyRegistration
        where TInterface : IGrain
        where TProxy : class, TInterface, IGrainProxyActivator<TProxy>
    {
        public void Apply(GrainProxyFactoryRegistry proxyRegistry, GrainInterfaceTypeRegistry interfaceRegistry)
        {
            // Derive GrainType: strip leading "I" convention (ICounterGrain → CounterGrain).
            string typeName = grainTypeName
                              ?? (typeof(TInterface).Name.StartsWith('I')
                                  ? typeof(TInterface).Name[1..]
                                  : typeof(TInterface).Name);

            var grainType = new GrainType(typeName);

            interfaceRegistry.Register(typeof(TInterface), grainType);

            proxyRegistry.Register<TInterface, TProxy>(static (grainId, invoker) => TProxy.Create(grainId, invoker));
        }
    }

    private sealed class ObserverProxyRegistration<TInterface, TProxy>
        : IObserverProxyRegistration
        where TInterface : IGrainObserver
        where TProxy : class, TInterface, IGrainObserverProxyActivator<TProxy>
    {
        public void Apply(ObserverProxyFactoryRegistry observerProxyRegistry)
        {
            observerProxyRegistry.Register<TInterface, TProxy>(
                static (grainId, invoker) => TProxy.Create(grainId, invoker));
        }
    }
}
