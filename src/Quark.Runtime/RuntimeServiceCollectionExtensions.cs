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

        // Compile-time behavior construction factories (populated by generated registrations only)
        services.TryAddSingleton<GrainBehaviorFactoryRegistry>();

        // Silo identity
        services.TryAddSingleton<ILocalSiloDetails, LocalSiloDetails>();

        // Grain directory
        services.TryAddSingleton<InMemoryGrainDirectory>();
        services.TryAddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());

        // Placement services
        services.TryAddSingleton<AttributePlacementStrategyResolver>();
        services.TryAddSingleton<IPlacementStrategyResolver>(sp => sp.GetRequiredService<AttributePlacementStrategyResolver>());
        services.TryAddSingleton<IPlacementDirector, PlacementDirector>();

        // Activation table
        services.TryAddSingleton<GrainActivationTable>();
        services.TryAddSingleton<IUserServiceProviderRegistry, UserServiceProviderRegistry>();
        services.TryAddSingleton<QuarkOnlyServiceProviderHolder>();

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
        //
        // ActivationScheduler (sharded ready queue, work-stealing sweep — see
        // docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md and
        // 2026-07-09-scheduler-wake-signal-sharding-design.md) is the default again as of GitHub
        // issue #167's fix: it previously had a reproducible bounded-worker-pool reentrancy deadlock
        // (a nested grain-to-grain call could keep enough workers "busy" waiting on each other's
        // replies to exhaust SchedulerMaxConcurrentActivations — full history in that type's class
        // remarks), which is why SimpleActivationScheduler was the fallback for a while. That's now
        // mitigated by a self-hosted stall watchdog that spins up transient overflow capacity when
        // the ready queue stops making progress — see
        // docs/superpowers/specs/2026-07-12-scheduler-reentrancy-deadlock-fix.md.
        // SimpleActivationScheduler (unbounded Task.Run per activation, no concurrency cap or other
        // QoS knobs) remains available for callers that want it explicitly — e.g. the
        // `--bare` PingPong benchmark mode.
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
    ///     Registers a grain behavior implementation and maps it to an explicit, compile-time-known
    ///     grain type key — never reflects. The behavior type is registered as <c>Transient</c> so
    ///     <c>ActivatorUtilities.CreateInstance</c> can construct it per call, unless
    ///     <paramref name="factory"/> is supplied, in which case behavior construction never reflects
    ///     either. The generated <c>QuarkRegistrations.g.cs</c> path always calls this overload.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="behaviorId">Explicit grain type key, known at compile time.</param>
    /// <param name="factory">
    ///     Explicit compile-time construction factory. When <c>null</c> (the default), behavior
    ///     construction falls back to <c>ActivatorUtilities</c> via the <c>Transient</c> registration
    ///     above — trim-safe, since the <typeparamref name="TBehavior"/> generic parameter is annotated
    ///     with <see cref="DynamicallyAccessedMemberTypes.PublicConstructors"/>.
    /// </param>
    public static IServiceCollection AddGrainBehavior<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        string behaviorId,
        Func<IServiceProvider, TBehavior>? factory = null)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
    {
        ArgumentNullException.ThrowIfNull(behaviorId);
        services.AddTransient<TBehavior>();

        var grainType = new GrainType(behaviorId);

        services.AddSingleton<IGrainBehaviorRegistration>(
            new GrainBehaviorRegistration(grainType, typeof(TBehavior)));

        if (factory is not null)
        {
            services.AddSingleton<IGrainBehaviorFactoryRegistration>(
                new GrainBehaviorFactoryRegistration(grainType, factory));
        }

        return services;
    }

    /// <summary>
    ///     Hand-wired convenience overload: registers a grain behavior without an explicit
    ///     <c>behaviorId</c>, deriving the grain type key by reflecting
    ///     <see cref="GrainBehaviorAttribute"/> (or falling back to the interface name) at runtime.
    ///     Never called by generated code — <c>QuarkRegistrations.g.cs</c> always supplies
    ///     <c>behaviorId</c> explicitly via the overload above. Prefer that overload (or the
    ///     <c>Quark.CodeGenerator</c> source generator) in an AOT-published production silo.
    /// </summary>
    [RequiresUnreferencedCode(
        "Reflects [GrainBehaviorAttribute] (or falls back to the interface name) off TBehavior to derive " +
        "the grain type key. Call the AddGrainBehavior<TInterface,TBehavior>(string behaviorId, ...) " +
        "overload directly, or use the Quark.CodeGenerator source generator, to avoid this at runtime.")]
    public static IServiceCollection AddGrainBehavior<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        Func<IServiceProvider, TBehavior>? factory = null)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
        => services.AddGrainBehavior<TInterface, TBehavior>(GetGrainTypeKey<TInterface, TBehavior>(), factory);

    /// <summary>
    ///     Registers <typeparamref name="TBehavior"/>'s <see cref="IGrainUserServiceProviderFactory" />
    ///     opt-in. Called by the generated <c>QuarkRegistrations.g.cs</c> path with an explicit
    ///     <paramref name="behaviorId"/> always supplied; use this overload directly for hand-wired
    ///     (non-generator) test/sample registrations too.
    /// </summary>
    /// <param name="behaviorId">
    ///     Explicit grain type key. Must match the <c>behaviorId</c> passed to the corresponding
    ///     <see cref="AddGrainBehavior{TInterface,TBehavior}"/> call — otherwise this registers under a
    ///     different key and silently never applies. When <c>null</c>, falls back to reflecting
    ///     <see cref="GrainBehaviorAttribute"/> or the interface name, exactly as
    ///     <see cref="AddGrainBehavior{TInterface,TBehavior}"/> does when its own <c>behaviorId</c> is
    ///     omitted.
    /// </param>
    public static IServiceCollection AddGrainUserServiceProviderFactory<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services,
        string? behaviorId = null)
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface, IGrainUserServiceProviderFactory
    {
#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) registrations.
        string key = behaviorId ?? GetGrainTypeKey<TInterface, TBehavior>();
#pragma warning restore IL2026
        services.AddSingleton<IUserServiceProviderFactoryRegistration>(
            new UserServiceProviderFactoryRegistration(new GrainType(key), TBehavior.CreateUserServiceProvider));

        return services;
    }

    /// <summary>
    ///     Explicitly registers the placement strategy for a behavior class, bypassing the runtime
    ///     attribute-reflection fallback in <see cref="AttributePlacementStrategyResolver"/>.
    /// </summary>
    public static IServiceCollection AddGrainPlacementStrategy<TBehavior>(
        this IServiceCollection services,
        PlacementStrategy strategy)
        where TBehavior : class, IGrainBehavior
    {
        ArgumentNullException.ThrowIfNull(strategy);
        services.AddSingleton<IGrainPlacementStrategyRegistration>(
            new GrainPlacementStrategyRegistration(typeof(TBehavior), strategy));
        return services;
    }

    /// <summary>
    ///     Registers a Quark-owned scoped service AND captures a replayable marker so it can be
    ///     reconstructed onto a separate "Quark-only" satellite <see cref="IServiceCollection" /> at
    ///     startup (see <see cref="IGrainUserServiceProviderFactory" />). Used by the source generator
    ///     for per-behavior accessor registrations (<c>IActivationMemory&lt;T&gt;</c> etc.) — every
    ///     assembly's accessors become replayable this way, whether or not any behavior opts in.
    /// </summary>
    public static IServiceCollection AddQuarkOwnedScoped<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> factory)
        where TService : class
    {
        services.AddScoped(factory);
        services.AddSingleton<IQuarkOwnedServiceRegistration>(new QuarkOwnedServiceRegistration<TService>(factory));
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
        services.AddQuarkOwnedScoped<IEagerActivationMemory<T>>(static sp =>
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

    internal interface IGrainBehaviorFactoryRegistration
    {
        void Apply(GrainBehaviorFactoryRegistry registry);
    }

    private sealed class GrainBehaviorFactoryRegistration(GrainType grainType, Func<IServiceProvider, IGrainBehavior> factory)
        : IGrainBehaviorFactoryRegistration
    {
        public void Apply(GrainBehaviorFactoryRegistry registry) => registry.Register(grainType, factory);
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

    internal interface IQuarkOwnedServiceRegistration
    {
        void Apply(IServiceCollection satelliteServices);
    }

    private sealed class QuarkOwnedServiceRegistration<TService>(Func<IServiceProvider, TService> factory)
        : IQuarkOwnedServiceRegistration
        where TService : class
    {
        public void Apply(IServiceCollection satelliteServices) => satelliteServices.AddScoped(factory);
    }

    internal interface IUserServiceProviderFactoryRegistration
    {
        void Apply(IUserServiceProviderRegistry registry, IServiceProvider rootServices);
    }

    private sealed class UserServiceProviderFactoryRegistration(GrainType grainType, Func<IServiceProvider, IServiceProvider> factory)
        : IUserServiceProviderFactoryRegistration
    {
        public void Apply(IUserServiceProviderRegistry registry, IServiceProvider rootServices)
            => registry.Register(grainType, factory(rootServices));
    }

    internal interface IGrainPlacementStrategyRegistration
    {
        void Apply(AttributePlacementStrategyResolver registry);
    }

    private sealed class GrainPlacementStrategyRegistration(Type behaviorType, PlacementStrategy strategy)
        : IGrainPlacementStrategyRegistration
    {
        public void Apply(AttributePlacementStrategyResolver registry) => registry.Register(behaviorType, strategy);
    }

    [RequiresUnreferencedCode(
        "Reflects [GrainBehaviorAttribute] off TBehavior when AddGrainBehavior<,>() is called without an " +
        "explicit behaviorId — i.e. hand-wired (non-generator) registrations. The generated " +
        "QuarkRegistrations.g.cs path always supplies behaviorId explicitly and never calls this.")]
    private static string GetGrainTypeKey<TInterface, [DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>()
        where TInterface : IGrain
        where TBehavior : class, IGrainBehavior, TInterface
        => typeof(TBehavior).GetCustomAttributes(typeof(GrainBehaviorAttribute), false)
            is GrainBehaviorAttribute[] { Length: > 0 } attrs
            ? attrs[0].BehaviorId
            : typeof(TInterface).Name.StartsWith('I') ? typeof(TInterface).Name[1..] : typeof(TInterface).Name;
}
