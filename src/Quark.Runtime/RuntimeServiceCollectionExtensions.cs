using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Dependency-injection extension methods for Quark runtime services.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core Quark runtime services:
    /// <list type="bullet">
    ///   <item><see cref="LifecycleSubject"/> — silo lifecycle manager</item>
    ///   <item><see cref="GrainTypeRegistry"/> — grain type resolution</item>
    ///   <item><see cref="InMemoryGrainDirectory"/> — single-node grain directory</item>
    ///   <item><see cref="DefaultGrainActivator"/> — DI-backed grain creation</item>
    ///   <item><see cref="SiloHostedService"/> — silo host lifecycle integration</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddQuarkRuntime(this IServiceCollection services)
    {
        // Silo lifecycle — singleton so everything shares the same ordered start/stop.
        services.TryAddSingleton<LifecycleSubject>();

        // Grain type registry — populated via AddGrain<T>() calls.
        services.TryAddSingleton<GrainTypeRegistry>();
        services.TryAddSingleton<IGrainTypeRegistry>(sp =>
            sp.GetRequiredService<GrainTypeRegistry>());

        // Grain directory — in-memory for single-node / testing; swap for clustered.
        services.TryAddSingleton<InMemoryGrainDirectory>();
        services.TryAddSingleton<IGrainDirectory>(sp =>
            sp.GetRequiredService<InMemoryGrainDirectory>());

        // Grain activator.
        services.TryAddSingleton<IGrainActivator, DefaultGrainActivator>();

        // Hosted service drives the silo lifecycle.
        services.AddHostedService<SiloHostedService>();

        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TGrain"/> so it can be resolved from the DI container
    /// and activated by the runtime.
    /// </summary>
    public static IServiceCollection AddGrain<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TGrain>(this IServiceCollection services)
        where TGrain : Grain
    {
        // Register as transient — the runtime creates one instance per activation.
        services.AddTransient<TGrain>();

        // Also register by base type so DefaultGrainActivator can GetRequiredService(grainClass).
        services.TryAddTransient<Grain>(sp => sp.GetRequiredService<TGrain>());

        // Post-startup: register in the type registry.
        // We use IStartupFilter-like pattern via a hosted action instead of a factory-time side
        // effect so that the DI container is not mutated after Build().
        services.AddSingleton<IGrainRegistration>(
            new GrainRegistration(new GrainType(typeof(TGrain).Name), typeof(TGrain)));

        return services;
    }

    // ----- internal helpers ------------------------------------------------

    private interface IGrainRegistration
    {
        void Apply(GrainTypeRegistry registry);
    }

    private sealed class GrainRegistration(GrainType grainType, Type grainClass)
        : IGrainRegistration
    {
        public void Apply(GrainTypeRegistry registry) =>
            registry.Register(grainType, grainClass);
    }
}
