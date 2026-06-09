using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Diagnostics.Abstractions;

namespace Quark.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Quark diagnostic system with a custom listener.
    ///     Multiple calls append listeners — all receive every event via <see cref="CompositeDiagnosticListener" />.
    /// </summary>
    public static IServiceCollection AddQuarkDiagnostics<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TListener>(
        this IServiceCollection services)
        where TListener : class, IQuarkDiagnosticListener
    {
        services.AddSingleton<IQuarkDiagnosticListener, TListener>();
        EnsureComposite(services);
        return services;
    }

    /// <summary>
    ///     Registers the Quark diagnostic system with a pre-built listener instance.
    ///     Multiple calls append listeners.
    /// </summary>
    public static IServiceCollection AddQuarkDiagnostics(
        this IServiceCollection services, IQuarkDiagnosticListener listener)
    {
        services.AddSingleton(listener);
        EnsureComposite(services);
        return services;
    }

    /// <summary>
    ///     Adds the <see cref="StuckGrainDetector" /> background service.
    ///     Requires that <c>AddQuarkDiagnostics</c> has already been called so there is a
    ///     registered <see cref="IQuarkDiagnosticListener" /> to receive the stuck events.
    /// </summary>
    public static IServiceCollection AddQuarkStuckGrainDetector(
        this IServiceCollection services,
        Action<DiagnosticOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        else
            services.TryAddSingleton(Microsoft.Extensions.Options.Options.Create(new DiagnosticOptions()));

        services.AddHostedService<StuckGrainDetector>();
        return services;
    }

    // -----------------------------------------------------------------------

    private static void EnsureComposite(IServiceCollection services)
    {
        // Replace any previous composite registration so it wraps ALL registered listeners.
        // We do this by removing the old composite (if any) and re-registering.
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(CompositeDiagnosticListener))
            {
                services.RemoveAt(i);
                break;
            }
        }

        services.AddSingleton<CompositeDiagnosticListener>();

        // The runtime resolves IQuarkDiagnosticListener; point it at the composite.
        // Remove any existing IQuarkDiagnosticListener → composite binding to avoid duplicates.
        for (int i = services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor d = services[i];
            if (d.ServiceType == typeof(IQuarkDiagnosticListener)
                && d.ImplementationType == typeof(CompositeDiagnosticListener))
            {
                services.RemoveAt(i);
                break;
            }
        }

        services.AddSingleton<IQuarkDiagnosticListener>(
            sp => sp.GetRequiredService<CompositeDiagnosticListener>());
    }
}
