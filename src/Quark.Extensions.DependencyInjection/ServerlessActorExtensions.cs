// Copyright (c) Quark Framework. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Hosting;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring serverless actor hosting with auto-scaling from zero.
/// </summary>
public static class ServerlessActorExtensions
{
    /// <summary>
    /// Adds serverless actor hosting with automatic idle deactivation.
    /// Enables auto-scaling from zero for pay-per-use scenarios.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="configure">Optional action to configure serverless options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithServerlessActors(
        this IQuarkSiloBuilder builder,
        Action<ServerlessActorOptions>? configure = null)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Configure options
        var options = new ServerlessActorOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        // Register deactivation policy
        builder.Services.TryAddSingleton<IActorDeactivationPolicy>(sp =>
        {
            var opts = sp.GetRequiredService<ServerlessActorOptions>();
            return new IdleTimeoutDeactivationPolicy(opts.IdleTimeout);
        });

        // Register the idle deactivation service
        builder.Services.AddSingleton<IdleDeactivationService>();
        builder.Services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<IdleDeactivationService>());

        return builder;
    }

    /// <summary>
    /// Adds serverless actor hosting with a custom deactivation policy.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="policyFactory">Factory function to create the deactivation policy.</param>
    /// <param name="configure">Optional action to configure serverless options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithServerlessActors(
        this IQuarkSiloBuilder builder,
        Func<IServiceProvider, IActorDeactivationPolicy> policyFactory,
        Action<ServerlessActorOptions>? configure = null)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (policyFactory == null)
        {
            throw new ArgumentNullException(nameof(policyFactory));
        }

        // Configure options
        var options = new ServerlessActorOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);

        // Register custom deactivation policy
        builder.Services.TryAddSingleton(policyFactory);

        // Register the idle deactivation service
        builder.Services.AddSingleton<IdleDeactivationService>();
        builder.Services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<IdleDeactivationService>());

        return builder;
    }
}
