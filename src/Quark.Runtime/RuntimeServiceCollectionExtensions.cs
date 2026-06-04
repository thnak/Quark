using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Dependency-injection extension methods for Quark runtime services.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    ///     Registers all core Quark runtime services:
    ///     <list type="bullet">
    ///         <item><see cref="LifecycleSubject" /> — silo lifecycle manager</item>
    ///         <item><see cref="GrainTypeRegistry" /> — grain type resolution</item>
    ///         <item><see cref="InMemoryGrainDirectory" /> — single-node grain directory</item>
    ///         <item><see cref="DefaultGrainActivator" /> — DI-backed grain creation</item>
    ///         <item><see cref="GrainActivationTable" /> — per-silo activation tracker</item>
    ///         <item><see cref="TransportGrainDispatcherRegistry" /> — per-grain-type transport dispatchers</item>
    ///         <item><see cref="LocalGrainCallInvoker" /> — in-process call router</item>
    ///         <item><see cref="SiloHostedService" /> — silo host lifecycle integration</item>
    ///     </list>
    /// </summary>
    public static IServiceCollection AddQuarkRuntime(this IServiceCollection services)
    {
        // Silo lifecycle — singleton so everything shares the same ordered start/stop.
        services.TryAddSingleton<LifecycleSubject>();

        // Grain type registry — populated via AddGrain<T>() calls.
        services.TryAddSingleton<GrainTypeRegistry>();
        services.TryAddSingleton<IGrainTypeRegistry>(sp => sp.GetRequiredService<GrainTypeRegistry>());

        // Grain directory — in-memory for single-node / testing; swap for clustered.
        services.TryAddSingleton<InMemoryGrainDirectory>();
        services.TryAddSingleton<IGrainDirectory>(sp => sp.GetRequiredService<InMemoryGrainDirectory>());

        // Grain activator.
        services.TryAddSingleton<IGrainActivator, DefaultGrainActivator>();

        // Placement services — strategy resolution and target-silo selection.
        services.TryAddSingleton<IPlacementStrategyResolver, AttributePlacementStrategyResolver>();
        services.TryAddSingleton<IPlacementDirector, PlacementDirector>();

        // Activation table — live activations on this silo.
        services.TryAddSingleton<GrainActivationTable>();

        // Transport dispatcher registry — populated via AddGrainTransportDispatcher().
        services.TryAddSingleton<TransportGrainDispatcherRegistry>();

        // Observer registry — populated via CreateObjectReference<T>().
        services.TryAddSingleton<ObserverRegistry>();

        // Local in-process call invoker.
        services.TryAddSingleton<LocalGrainCallInvoker>(sp => new LocalGrainCallInvoker(
            sp.GetRequiredService<GrainActivationTable>(),
            sp.GetRequiredService<IGrainActivator>(),
            sp.GetRequiredService<IGrainTypeRegistry>(),
            sp.GetRequiredService<IGrainDirectory>(),
            sp,
            sp.GetRequiredService<IOptions<SiloRuntimeOptions>>(),
            sp.GetRequiredService<ILogger<LocalGrainCallInvoker>>(),
            sp.GetRequiredService<ILogger<GrainActivation>>(),
            sp.GetService<ObserverRegistry>()));
        services.TryAddSingleton<IGrainCallInvoker>(sp => sp.GetRequiredService<LocalGrainCallInvoker>());

        // Message dispatch / pump services for transport-routed grain calls.
        services.TryAddSingleton<MessageSerializer>();
        services.TryAddSingleton<GrainMessageSerializer>();
        services.TryAddSingleton<IMessageDispatcher, MessageDispatcher>();
        services.TryAddSingleton<SiloMessagePump>();

        // Hosted service drives the silo lifecycle.
        services.AddHostedService<SiloHostedService>();

        return services;
    }

    /// <summary>
    ///     Registers <typeparamref name="TGrain" /> so it can be resolved from the DI container
    ///     and activated by the runtime.
    /// </summary>
    public static IServiceCollection AddGrain<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TGrain>(
        this IServiceCollection services)
        where TGrain : Grain
    {
        // Register as transient — the runtime creates one instance per activation.
        services.AddTransient<TGrain>();

        // Also register by base type so DefaultGrainActivator can GetRequiredService(grainClass).
        services.TryAddTransient<Grain>(sp => sp.GetRequiredService<TGrain>());

        // Post-startup: register in the type registry.
        services.AddSingleton<IGrainRegistration>(
            new GrainRegistration(new GrainType(typeof(TGrain).Name), typeof(TGrain)));

        return services;
    }

    /// <summary>
    ///     Registers a generated or hand-written <see cref="IGrainActivatorFactory" />.
    ///     This provides an AOT-safe activation path which avoids reflection-based construction.
    /// </summary>
    public static IServiceCollection AddGrainActivatorFactory<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TFactory>(
        this IServiceCollection services)
        where TFactory : class, IGrainActivatorFactory
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGrainActivatorFactory, TFactory>());
        return services;
    }

    /// <summary>
    ///     Registers a generated or hand-written <see cref="ITransportGrainDispatcher" /> for
    ///     the specified <paramref name="grainType" />.
    ///     The dispatcher maps incoming transport method IDs to strongly-typed invokable structs,
    ///     enabling AOT-safe cross-silo dispatch without <c>object?[]</c> boxing.
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

    // ----- internal helpers ------------------------------------------------

    internal interface IGrainRegistration
    {
        void Apply(GrainTypeRegistry registry);
    }

    private sealed class GrainRegistration(GrainType grainType, Type grainClass)
        : IGrainRegistration
    {
        public void Apply(GrainTypeRegistry registry)
        {
            registry.Register(grainType, grainClass);
        }
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
