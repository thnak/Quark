using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Core.Reminders;
using Quark.Core.Streaming;
using Quark.Hosting;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring Quark Silo services.
/// </summary>
public static class QuarkSiloServiceCollectionExtensions
{
    public static void UseQuark(this IHostApplicationBuilder hostBuilder, Action<QuarkSiloOptions>? configure = null,
        Action<IQuarkSiloBuilder>? siloConfigure = null)
    {
        if (hostBuilder == null)
        {
            throw new ArgumentNullException(nameof(hostBuilder));
        }

        var siloBuilder = hostBuilder.Services.AddQuarkSilo(configure);
        siloConfigure?.Invoke(siloBuilder);
        hostBuilder.Services.AddQuarkClient();
        hostBuilder.Services.AddActorActivityTracking();
    }

    /// <summary>
    /// Adds a Quark Silo to the service collection with full actor hosting capabilities.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure silo options.</param>
    /// <returns>A builder for further configuration.</returns>
    public static IQuarkSiloBuilder AddQuarkSilo(
        this IServiceCollection services,
        Action<QuarkSiloOptions>? configure = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        // Configure options
        var options = new QuarkSiloOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Register core services
        services.TryAddSingleton<IActorFactory, ActorFactory>();

        // Register silo
        services.AddSingleton<QuarkSilo>();
        services.AddSingleton<IQuarkSilo>(sp => sp.GetRequiredService<QuarkSilo>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<QuarkSilo>());

        return new QuarkSiloBuilder(services, options);
    }

    /// <summary>
    /// Adds ReminderTickManager to the silo.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="tickInterval">Optional tick interval. Defaults to 1 second.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithReminders(
        this IQuarkSiloBuilder builder,
        TimeSpan? tickInterval = null)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton(sp =>
        {
            var reminderTable = sp.GetRequiredService<Abstractions.Reminders.IReminderTable>();
            var options = sp.GetRequiredService<QuarkSiloOptions>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ReminderTickManager>>();
            return new ReminderTickManager(reminderTable, options.SiloId ?? Guid.NewGuid().ToString("N"), logger,
                tickInterval);
        });

        return builder;
    }

    /// <summary>
    /// Adds StreamBroker to the silo.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithStreaming(this IQuarkSiloBuilder builder)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        builder.Services.AddSingleton(sp =>
        {
            var actorFactory = sp.GetRequiredService<IActorFactory>();
            var logger = sp.GetService<ILogger<StreamBroker>>();
            return new StreamBroker(actorFactory, logger);
        });

        return builder;
    }
}

/// <summary>
/// Builder for configuring Quark Silo services.
/// </summary>
public interface IQuarkSiloBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the silo options.
    /// </summary>
    QuarkSiloOptions Options { get; }
}

internal sealed class QuarkSiloBuilder : IQuarkSiloBuilder
{
    public QuarkSiloBuilder(IServiceCollection services, QuarkSiloOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public QuarkSiloOptions Options { get; }
}