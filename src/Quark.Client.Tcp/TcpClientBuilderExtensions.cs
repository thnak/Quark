using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Client;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Hosting;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Streaming.Abstractions;
using Quark.Transport.Tcp;

namespace Quark.Client.Tcp;

/// <summary>
///     Extension methods for wiring up the TCP gateway client on an <see cref="IClientBuilder" />.
/// </summary>
public static class TcpClientBuilderExtensions
{
    /// <summary>
    ///     Configures the client to connect to a gateway silo on <c>localhost:{gatewayPort}</c>.
    ///     Drop-in equivalent of Orleans' <c>UseLocalhostClustering()</c> on the client side.
    /// </summary>
    public static IClientBuilder UseLocalhostGateway(
        this IClientBuilder builder,
        int gatewayPort = 30000)
        => builder.UseTcpGateway(o => o.GatewayEndpoint = new IPEndPoint(IPAddress.Loopback, gatewayPort));

    /// <summary>
    ///     Configures the client to connect to a gateway silo using the provided endpoint configuration.
    /// </summary>
    public static IClientBuilder UseTcpGateway(
        this IClientBuilder builder,
        Action<TcpGatewayClientOptions> configure)
    {
        IServiceCollection services = builder.Services;

        services.Configure(configure);

        services.TryAddSingleton<GrainProxyFactoryRegistry>();
        services.TryAddSingleton<GrainInterfaceTypeRegistry>();

        services.AddTcpTransport();

        services.RemoveAll<LocalClusterClient>();
        services.RemoveAll<LocalGrainFactory>();
        services.RemoveAll<IClusterClient>();
        services.RemoveAll<IGrainFactory>();

        // Observer back-channel support
        services.TryAddSingleton<ObserverRegistry>();
        services.TryAddSingleton<ObserverProxyFactoryRegistry>();
        services.TryAddSingleton<ObserverTransportDispatcherRegistry>();
        services.TryAddSingleton<TcpObserverDispatcher>();

        services.TryAddSingleton<MessageSerializer>();
        services.TryAddSingleton<GrainMessageSerializer>();
        services.TryAddSingleton<TcpStreamPushDispatcher>();
        services.TryAddSingleton(sp => new TcpGatewayConnection(
            sp.GetRequiredService<TcpTransport>(),
            sp.GetRequiredService<MessageSerializer>(),
            sp.GetService<TcpStreamPushDispatcher>(),
            sp.GetService<TcpObserverDispatcher>()));
        services.TryAddSingleton(sp => new TcpGatewayCallInvoker(
            sp.GetRequiredService<TcpGatewayConnection>(),
            sp.GetRequiredService<GrainMessageSerializer>(),
            sp.GetService<ObserverRegistry>()));
        services.TryAddSingleton(sp => new TcpGatewayGrainFactory(
            sp.GetRequiredService<GrainProxyFactoryRegistry>(),
            sp.GetRequiredService<GrainInterfaceTypeRegistry>(),
            sp.GetRequiredService<TcpGatewayCallInvoker>(),
            sp.GetService<ObserverRegistry>(),
            sp.GetService<ObserverProxyFactoryRegistry>(),
            sp.GetRequiredService<TcpGatewayConnection>()));
        services.TryAddSingleton<TcpGatewayClusterClient>();
        services.TryAddSingleton<IClusterClient>(sp => sp.GetRequiredService<TcpGatewayClusterClient>());
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<TcpGatewayGrainFactory>());
        services.AddHostedService<TcpClientStartupService>();

        return builder;
    }

    /// <summary>
    ///     Registers a named <see cref="IStreamProvider" /> backed by the TCP gateway connection.
    ///     Call after <see cref="UseTcpGateway" /> (or <see cref="UseLocalhostGateway" />).
    /// </summary>
    public static IClientBuilder AddTcpClientStreams(
        this IClientBuilder builder,
        string providerName)
    {
        builder.Services.AddKeyedSingleton<IStreamProvider>(providerName,
            (sp, _) => new TcpClientStreamProvider(
                providerName,
                sp.GetRequiredService<TcpGatewayConnection>(),
                sp.GetRequiredService<TcpStreamPushDispatcher>(),
                sp.GetRequiredService<ICodecProvider>()));
        return builder;
    }

    /// <summary>
    ///     Registers an observer proxy factory so that
    ///     <see cref="IGrainFactory.CreateObjectReference{TGrainObserver}" /> can wrap a local
    ///     <typeparamref name="TInterface" /> implementation in a proxy for TCP transport.
    ///     Also call <see cref="AddObserverTransportDispatcher{TInterface}" /> so incoming
    ///     <c>ObserverInvoke</c> frames can be dispatched to the local implementation.
    /// </summary>
    public static IClientBuilder AddObserverProxy<TInterface, TProxy>(
        this IClientBuilder builder)
        where TInterface : IGrainObserver
        where TProxy : class, TInterface, IGrainObserverProxyActivator<TProxy>
    {
        builder.Services.AddObserverProxy<TInterface, TProxy>();
        return builder;
    }

    /// <summary>
    ///     Registers the <see cref="ITransportGrainDispatcher" /> that deserialises incoming
    ///     <c>ObserverInvoke</c> frames for observer interface <typeparamref name="TInterface" />
    ///     and dispatches them to the locally registered implementation.
    ///     The grain type is derived automatically as <c>observer:{typeof(TInterface).Name}</c>.
    /// </summary>
    public static IClientBuilder AddObserverTransportDispatcher<TInterface>(
        this IClientBuilder builder,
        ITransportGrainDispatcher dispatcher)
        where TInterface : IGrainObserver
    {
        var grainType = new GrainType($"observer:{typeof(TInterface).Name}");
        builder.Services.AddSingleton<IObserverDispatcherRegistration>(
            new ObserverDispatcherRegistrationImpl(grainType, dispatcher));
        return builder;
    }

    // -----------------------------------------------------------------------

    /// <summary>Deferred registration applied at startup by <see cref="TcpClientStartupService" />.</summary>
    public interface IObserverDispatcherRegistration
    {
        void Apply(ObserverTransportDispatcherRegistry registry);
    }

    private sealed class ObserverDispatcherRegistrationImpl(GrainType grainType, ITransportGrainDispatcher dispatcher)
        : IObserverDispatcherRegistration
    {
        public void Apply(ObserverTransportDispatcherRegistry registry)
            => registry.Register(grainType, dispatcher);
    }
}
