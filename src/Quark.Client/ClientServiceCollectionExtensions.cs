using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Client;

/// <summary>
/// Dependency-injection extension methods for the Quark client.
/// </summary>
public static class ClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-process cluster client and its dependencies.
    /// The resulting <see cref="IClusterClient"/> routes calls directly to the local silo
    /// (cohosted scenario, equivalent to running client and silo in the same process).
    /// </summary>
    public static IServiceCollection AddLocalClusterClient(this IServiceCollection services)
    {
        services.TryAddSingleton<GrainProxyFactoryRegistry>();
        services.TryAddSingleton<GrainInterfaceTypeRegistry>();
        services.TryAddSingleton<LocalGrainFactory>();
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<LocalGrainFactory>());
        services.TryAddSingleton<LocalClusterClient>();
        services.TryAddSingleton<IClusterClient>(sp => sp.GetRequiredService<LocalClusterClient>());
        services.AddHostedService<ClientStartupService>();
        return services;
    }

    /// <summary>
    /// Registers a grain proxy factory so that <see cref="IClusterClient.GetGrain{T}"/> can
    /// create a proxy for <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The grain interface (e.g. <c>ICounterGrain</c>).</typeparam>
    /// <typeparam name="TProxy">
    ///   The generated proxy class that implements <typeparamref name="TInterface"/>
    ///   and routes calls through <see cref="IGrainCallInvoker"/>.
    /// </typeparam>
    /// <param name="grainTypeName">
    ///   Optional override for the grain-type name (defaults to the implementation class name
    ///   without the leading "I", e.g. <c>CounterGrain</c>).
    /// </param>
    public static IServiceCollection AddGrainProxy<
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProxy>(
        this IServiceCollection services,
        string? grainTypeName = null)
        where TInterface : IGrain
        where TProxy : class, TInterface
    {
        services.AddSingleton<IProxyRegistration>(
            new ProxyRegistration<TInterface, TProxy>(grainTypeName));
        return services;
    }

    // -----------------------------------------------------------------------

    internal interface IProxyRegistration
    {
        void Apply(GrainProxyFactoryRegistry proxyRegistry, GrainInterfaceTypeRegistry interfaceRegistry);
    }

    private sealed class ProxyRegistration<TInterface, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProxy>(string? grainTypeName)
        : IProxyRegistration
        where TInterface : IGrain
        where TProxy : class, TInterface
    {
        public void Apply(GrainProxyFactoryRegistry proxyRegistry, GrainInterfaceTypeRegistry interfaceRegistry)
        {
            // Derive GrainType: strip leading "I" convention (ICounterGrain → CounterGrain).
            var typeName = grainTypeName
                ?? (typeof(TInterface).Name.StartsWith('I')
                    ? typeof(TInterface).Name[1..]
                    : typeof(TInterface).Name);

            var grainType = new GrainType(typeName);

            interfaceRegistry.Register(typeof(TInterface), grainType);

            // Factory: create TProxy(grainId, invoker) via constructor.
            proxyRegistry.Register<TInterface, TProxy>(
                (grainId, invoker) => CreateProxy(grainId, invoker));
        }

        private static TProxy CreateProxy(GrainId grainId, IGrainCallInvoker invoker)
        {
            // TProxy is expected to have a constructor (GrainId, IGrainCallInvoker).
            return (TProxy)Activator.CreateInstance(typeof(TProxy), grainId, invoker)!;
        }
    }
}
