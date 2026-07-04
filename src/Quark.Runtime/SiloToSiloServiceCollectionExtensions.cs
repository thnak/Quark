using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Hosting;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime.Clustering;
using Quark.Runtime.StatelessWorker;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Registers the silo-to-silo networked transport.
///     Call from a clustering provider (e.g. <c>UseRedisClustering</c>) or explicitly before
///     a multi-silo deployment.
///     <c>UseLocalhostClustering</c> does <em>not</em> call this — the in-process shared
///     router is strictly cheaper and already correct for same-process silos.
/// </summary>
public static class SiloToSiloServiceCollectionExtensions// TODO did not implemented or used in any elsewhere
{
    public static IServiceCollection AddSiloToSiloTransport(this IServiceCollection services)
    {
        // Networked router — values are SiloCallInvoker instances installed by PeerConnectionManager.
        services.TryAddSingleton<NetworkedSiloRouter>();
        services.TryAddSingleton<ISiloRouter>(sp => sp.GetRequiredService<NetworkedSiloRouter>());

        // Updatable membership snapshot — refreshed by PeerConnectionManager each heartbeat.
        services.TryAddSingleton<DefaultClusterMembershipSnapshot>(sp =>
            new DefaultClusterMembershipSnapshot(
                sp.GetRequiredService<IOptions<SiloRuntimeOptions>>().Value.SiloAddress));
        services.TryAddSingleton<IClusterMembershipSnapshot>(sp =>
            sp.GetRequiredService<DefaultClusterMembershipSnapshot>());

        // PeerConnectionManager drives router register/unregister + snapshot updates.
        services.AddHostedService<PeerConnectionManager>();

        // Local-terminal invoker for MessageDispatcher to use on x-quark-hop requests.
        // siloRouter: null → TryRouteRemote returns null immediately → always activates locally.
        // placementDirector: null → placement path skipped → prevents any further forwarding.
        services.AddKeyedSingleton<IGrainCallInvoker>("silo-terminal", (sp, _) => new LocalGrainCallInvoker(
            sp.GetRequiredService<GrainActivationTable>(),
            sp.GetRequiredService<IGrainTypeRegistry>(),
            sp.GetRequiredService<IGrainDirectory>(),
            sp,
            sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            sp.GetRequiredService<ILogger<LocalGrainCallInvoker>>(),
            sp.GetRequiredService<ILogger<GrainActivation>>(),
            sp.GetService<ObserverRegistry>(),
            copierProvider: sp.GetService<ICopierProvider>(),
            siloRouter: null,
            tcpObserverTable: sp.GetService<TcpClientObserverTable>(),
            diagnostics: sp.GetService<IQuarkDiagnosticListener>(),
            placementDirector: null,
            membershipSnapshot: null,
            statelessWorkerRouter: sp.GetService<StatelessWorkerRouter>()));

        return services;
    }
}
