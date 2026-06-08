using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime.Clustering;
using Quark.Serialization.Abstractions.Abstractions;

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
            siloRouter: sp.GetService<ISiloRouter>()));
        services.TryAddSingleton<IGrainCallInvoker>(sp => sp.GetRequiredService<LocalGrainCallInvoker>());

        // Message dispatch / pump services
        services.TryAddSingleton<MessageSerializer>();
        services.TryAddSingleton<GrainMessageSerializer>();
        services.TryAddSingleton<IMessageDispatcher>(sp => new MessageDispatcher(
            sp.GetRequiredService<TransportGrainDispatcherRegistry>(),
            sp.GetRequiredService<IGrainCallInvoker>(),
            sp.GetRequiredService<GrainMessageSerializer>(),
            sp.GetService<IGrainFactory>()));
        services.TryAddSingleton<SiloMessagePump>();

        // Gateway client subscription table
        services.TryAddSingleton<GatewayClientSubscriptionTable>();

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

        // Determine grain type key from [GrainBehavior] attribute or interface name
        string key = typeof(TBehavior).GetCustomAttributes(typeof(GrainBehaviorAttribute), false)
            is GrainBehaviorAttribute[] { Length: > 0 } attrs
            ? attrs[0].BehaviorId
            : typeof(TInterface).Name.TrimStart('I');

        services.AddSingleton<IGrainBehaviorRegistration>(
            new GrainBehaviorRegistration(new GrainType(key), typeof(TBehavior)));

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
}
