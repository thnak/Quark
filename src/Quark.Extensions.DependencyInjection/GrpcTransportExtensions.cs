using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Hosting;
using Quark.Networking.Abstractions;
using Quark.Transport.Grpc;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring gRPC transport with channel pooling and optimization for Quark silos.
/// </summary>
public static class GrpcTransportExtensions
{
    /// <summary>
    /// Adds gRPC transport to the Quark Silo with optional channel pooling.
    /// </summary>
    /// <param name="builder">The silo builder.</param>
    /// <param name="enableChannelPooling">Whether to enable gRPC channel pooling. Defaults to true.</param>
    /// <param name="configurePoolOptions">Optional action to configure channel pool options.</param>
    /// <returns>The builder for chaining.</returns>
    public static IQuarkSiloBuilder WithGrpcTransport(
        this IQuarkSiloBuilder builder,
        bool enableChannelPooling = true,
        Action<GrpcChannelPoolOptions>? configurePoolOptions = null)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        // Register channel pool if enabled
        if (enableChannelPooling)
        {
            builder.Services.TryAddSingleton(sp =>
            {
                var options = new GrpcChannelPoolOptions();
                configurePoolOptions?.Invoke(options);
                return new GrpcChannelPool(options);
            });
        }

        // Register gRPC transport
        builder.Services.TryAddSingleton<IQuarkTransport>(sp =>
        {
            var siloOptions = sp.GetRequiredService<QuarkSiloOptions>();
            var siloId = siloOptions.SiloId ?? Guid.NewGuid().ToString("N");
            var endpoint = $"{siloOptions.Address}:{siloOptions.Port}";
            
            var channelPool = enableChannelPooling ? sp.GetService<GrpcChannelPool>() : null;
            return new GrpcQuarkTransport(siloId, endpoint, channelPool);
        });

        return builder;
    }
}
