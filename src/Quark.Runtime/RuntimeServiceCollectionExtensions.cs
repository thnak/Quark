using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;
using Quark.Diagnostics.Abstractions;
using Quark.Persistence.Abstractions;
using Quark.Runtime.Clustering;
using Quark.Runtime.StatelessWorker;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Streaming.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Dependency-injection extension methods for Quark runtime services.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    ///     Registers all core Quark runtime services.
    /// </summary>
    public static IServiceCollection AddQuarkRuntime(this IServiceCollection services)
    {
        // Silo lifecycle
        services.TryAddSingleton<LifecycleSubject>();

        // Grain type registry
        services.TryAddSingleton<GrainTypeRegistry>();
        services.TryAddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());

        // Silo identity
        services.TryAddSingleton<ILocalSiloDetails, LocalSiloDetails>();

        // Grain directory
        services.TryAddSingleton<InMemoryGrainDirectory>();
        services.TryAddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());

        // Placement services
        services.TryAddSingleton<IPlacementStrategyResolver, AttributePlacementStrategyResolver>();
        services.TryAddSingleton<IPlacementDirector, PlacementDirector>();

        // Activation table
        services.TryAddSingleton<GrainActivationTable>();
        services.TryAddSingleton<IGrainScopeInitializerRegistry, GrainScopeInitializerRegistry>();

        // Per-call scope services — one instance per IServiceScope created per grain call
        services.TryAddScoped<ActivationShellAccessor>();
        services.TryAddScoped<IActivationShellAccessor>(sp => sp.GetRequiredService<ActivationShellAccessor>());
        services.TryAddScoped<CallContext>();
        services.TryAddScoped<ICallContext>(sp => sp.GetRequiredService<CallContext>());
        services.TryAddScoped<ICallContextSetter>(sp => sp.GetRequiredService<CallContext>());
        services.TryAddScoped<IBehaviorResolver, BehaviorResolver>();

        // Transport dispatcher registry
        services.TryAddSingleton<TransportGrainDispatcherRegistry>();

        // Observer registry
        services.TryAddSingleton<ObserverRegistry>();

        // TCP client-observer back-channel table (optional; populated by GatewayMessagePump)
        services.TryAddSingleton<TcpClientObserverTable>();

        // Diagnostic listener — NullDiagnosticListener unless the consumer calls AddQuarkDiagnostics.
        services.TryAddSingleton<IQuarkDiagnosticListener>(NullDiagnosticListener.Instance);

        // Activation scheduler — drives centralized drain dispatch; replaces per-activation loops.
        // Factory reads SiloRuntimeOptions so concurrency, drain budget, and queue capacity are configurable.
        services.TryAddSingleton<IActivationScheduler>(sp => new ActivationScheduler(
            sp.GetRequiredService<IOptions<SiloRuntimeOptions>>().Value,
            sp.GetService<IQuarkDiagnosticListener>(),
            sp.GetService<IOptions<DiagnosticOptions>>()?.Value));

        // Stateless-worker pool router (singleton; pool dictionaries keyed by logical grain id)
        services.TryAddSingleton<StatelessWorkerRouter>();

        // Local in-process call invoker
        services.TryAddSingleton<LocalGrainCallInvoker>(sp => new LocalGrainCallInvoker(
            sp.GetRequiredService<GrainActivationTable>(),
            sp.GetRequiredService<IGrainTypeRegistry>(),
            sp.GetRequiredService<IGrainDirectory>(),
            sp,
            sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            sp.GetRequiredService<ILogger<LocalGrainCallInvoker>>(),
            sp.GetRequiredService<ILogger<GrainActivation>>(),
            sp.GetService<ObserverRegistry>(),
            copierProvider: sp.GetService<ICopierProvider>(),
            siloRouter: sp.GetService<ISiloRouter>(),
            tcpObserverTable: sp.GetService<TcpClientObserverTable>(),
            diagnostics: sp.GetService<IQuarkDiagnosticListener>(),
            placementDirector: sp.GetService<IPlacementDirector>(),
            membershipSnapshot: sp.GetService<IClusterMembershipSnapshot>(),
            dedupStore: sp.GetService<IRequestDedupStore>(),
            statelessWorkerRouter: sp.GetService<StatelessWorkerRouter>()));
        services.TryAddSingleton<IGrainCallInvoker>(sp => sp.GetRequiredService<LocalGrainCallInvoker>());

        // Message dispatch / pump services
        services.TryAddSingleton<MessageSerializer>();
        services.TryAddSingleton<GrainMessageSerializer>();
        services.TryAddSingleton<IMessageDispatcher>(sp => new MessageDispatcher(
            sp.GetRequiredService<TransportGrainDispatcherRegistry>(),
            sp.GetRequiredService<IGrainCallInvoker>(),
            sp.GetRequiredService<GrainMessageSerializer>(),
            sp.GetService<IGrainFactory>(),
            terminalInvoker: sp.GetKeyedService<IGrainCallInvoker>("silo-terminal"),
            dedupStore: sp.GetService<IRequestDedupStore>()));
        services.TryAddSingleton<SiloMessagePump>();

        // Gateway client subscription table
        services.TryAddSingleton<GatewayClientSubscriptionTable>();

        // Implicit stream subscription activator — wires publish→activate for [ImplicitStreamSubscription] grains
        services.TryAddSingleton<IImplicitStreamActivator, LocalImplicitStreamActivator>();

        // Cascading termination — IActivationTerminator (singleton) + IActivationChildren (scoped per-call)
        services.TryAddSingleton<IActivationTerminator>(sp => new DefaultActivationTerminator(
            sp.GetRequiredService<GrainActivationTable>(),
            sp.GetService<IGrainDirectory>(),
            sp.GetService<ISiloRouter>(),
            sp.GetService<IQuarkDiagnosticListener>()));
        services.TryAddScoped<IActivationChildren>(sp =>
            new ActivationChildrenAccessor(
                sp.GetRequiredService<IActivationShellAccessor>().Shell.GetOrCreateChildRegistry()));

        // Idle-timeout grain collector
        services.AddHostedService<GrainIdleCollector>();

        // Startup DI validator — fails the silo if any behavior has missing dependencies
        services.AddHostedService<BehaviorStartupValidator>();

        // Silo lifecycle hosted service
        services.AddHostedService<SiloHostedService>();

        return services;
    }

    /// <summary>
    ///     Registers a grain behavior implementation and maps it to its grain type key.
    ///     The behavior type is registered as <c>Transient</c> so
    ///     <c>ActivatorUtilities.CreateInstance</c> can construct it per call.
    /// </summary>
    public static IServiceCollection AddGrainBehavior<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
    {
        services.AddTransient<TBehavior>();

        string key = GetGrainTypeKey<TInterface, TBehavior>();

        services.AddSingleton<IGrainBehaviorRegistration>(
            new GrainBehaviorRegistration(new GrainType(key), typeof(TBehavior)));

        return services;
    }

    /// <summary>
    ///     Registers a delegate that configures this grain type's per-call scope before
    ///     the behavior instance and its scoped dependencies are resolved.
    /// </summary>
    public static IServiceCollection AddGrainScopeInitializer<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        GrainScopeInitializer initializer)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
    {
        ArgumentNullException.ThrowIfNull(initializer);

        string key = GetGrainTypeKey<TInterface, TBehavior>();
        services.AddSingleton<IGrainScopeInitializerRegistration>(
            new GrainScopeInitializerRegistration(new GrainType(key), initializer));

        return services;
    }

    /// <summary>
    ///     Registers a scoped <see cref="IManagedActivationMemory{T}" /> backed by the activation shell.
    ///     The resource is lazily initialized on first <c>GetAsync()</c> call and cleaned up after
    ///     <c>OnDeactivateAsync</c> runs. Configure init/destroy delegates on the injected instance
    ///     (typically in the behavior constructor).
    /// </summary>
    public static IServiceCollection AddManagedActivationMemory<T>(
        this IServiceCollection services)
        where T : class
    {
        services.AddScoped<IManagedActivationMemory<T>>(static sp =>
            new ManagedActivationMemoryAccessor<T>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateManagedHolder<T>()));
        return services;
    }

    /// <summary>
    ///     Registers a scoped <see cref="IEagerActivationMemory{T}" /> backed by the activation shell.
    ///     The resource is initialized eagerly at activation time — after the behavior constructor fires
    ///     (which registers the factory via <c>Load()</c>) but before <c>OnActivateAsync</c>.
    ///     The factory receives the activation scope's <see cref="IServiceProvider"/>.
    ///     Cleanup runs after <c>OnDeactivateAsync</c>. NOT persisted to storage.
    /// </summary>
    public static IServiceCollection AddEagerActivationMemory<T>(
        this IServiceCollection services)
        where T : class
    {
        services.AddScoped<IEagerActivationMemory<T>>(static sp =>
            new EagerActivationMemoryAccessor<T>(
                sp.GetRequiredService<IActivationShellAccessor>()
                  .Shell.GetOrCreateEagerHolder<T>()));
        return services;
    }

    /// <summary>
    ///     Registers a transport dispatcher for the specified grain type.
    /// </summary>
    public static IServiceCollection AddGrainTransportDispatcher(
        this IServiceCollection services,
        GrainType grainType,
        ITransportGrainDispatcher dispatcher)
    {
        services.AddSingleton<IGrainTransportDispatcherRegistration>(
            new GrainTransportDispatcherRegistration(grainType, dispatcher));
        return services;
    }

    // ----- internal deferred-registration markers --------------------------

    internal interface IGrainBehaviorRegistration
    {
        void Apply(GrainTypeRegistry registry);
    }

    private sealed class GrainBehaviorRegistration(GrainType grainType, Type behaviorType)
        : IGrainBehaviorRegistration
    {
        public void Apply(GrainTypeRegistry registry) => registry.Register(grainType, behaviorType);
    }

    internal interface IGrainTransportDispatcherRegistration
    {
        void Apply(TransportGrainDispatcherRegistry registry);
    }

    private sealed class GrainTransportDispatcherRegistration(GrainType grainType, ITransportGrainDispatcher dispatcher)
        : IGrainTransportDispatcherRegistration
    {
        public void Apply(TransportGrainDispatcherRegistry registry) => registry.Register(grainType, dispatcher);
    }

    internal interface IGrainScopeInitializerRegistration
    {
        void Apply(IGrainScopeInitializerRegistry registry);
    }

    private sealed class GrainScopeInitializerRegistration(GrainType grainType, GrainScopeInitializer initializer)
        : IGrainScopeInitializerRegistration
    {
        public void Apply(IGrainScopeInitializerRegistry registry) => registry.Register(grainType, initializer);
    }

    private static string GetGrainTypeKey<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>()
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
        => typeof(TBehavior).GetCustomAttributes(typeof(GrainBehaviorAttribute), false)
            is GrainBehaviorAttribute[] { Length: > 0 } attrs
            ? attrs[0].BehaviorId
            : typeof(TInterface).Name.StartsWith('I') ? typeof(TInterface).Name[1..] : typeof(TInterface).Name;
}
