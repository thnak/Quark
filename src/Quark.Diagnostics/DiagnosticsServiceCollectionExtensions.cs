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
        services.AddSingleton<TListener>();
        services.AddSingleton(sp => new DiagnosticListenerRegistration(sp.GetRequiredService<TListener>()));
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
        services.AddSingleton(new DiagnosticListenerRegistration(listener));
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
        // TryAdd, not Add: every AddQuarkDiagnostics call routes its listener through a
        // DiagnosticListenerRegistration (see CompositeDiagnosticListener's constructor doc for why
        // that indirection exists), so the composite itself only needs registering once no matter
        // how many listeners are added.
        services.TryAddSingleton<CompositeDiagnosticListener>();

        // The runtime resolves IQuarkDiagnosticListener; point it at the composite.
        services.TryAddSingleton<IQuarkDiagnosticListener>(
            sp => sp.GetRequiredService<CompositeDiagnosticListener>());
    }
}

/// <summary>
///     Wraps a listener registered via <c>AddQuarkDiagnostics</c> so <see cref="CompositeDiagnosticListener" />
///     can enumerate exactly the listeners callers registered — never itself. See the constructor
///     doc on <see cref="CompositeDiagnosticListener" /> for why depending on
///     <c>IEnumerable&lt;IQuarkDiagnosticListener&gt;</c> directly would self-reference and deadlock.
/// </summary>
public sealed class DiagnosticListenerRegistration(IQuarkDiagnosticListener listener)
{
    public IQuarkDiagnosticListener Listener { get; } = listener;
}
