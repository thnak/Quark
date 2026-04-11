using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Hosting;
using Quark.Serialization;

namespace Quark.Core;

/// <summary>
/// Extension methods for registering Quark services into an <see cref="IServiceCollection"/>.
/// </summary>
public static class QuarkServiceCollectionExtensions
{
    /// <summary>
    /// Adds core Quark silo services to <paramref name="services"/>.
    /// </summary>
    public static IServiceCollection AddQuarkSilo(
        this IServiceCollection services,
        Action<ISiloBuilder>? configure = null)
    {
        services.AddQuarkSerialization();

        var builder = new DefaultSiloBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    /// <summary>
    /// Adds core Quark client services to <paramref name="services"/>.
    /// </summary>
    public static IServiceCollection AddQuarkClient(
        this IServiceCollection services,
        Action<IClientBuilder>? configure = null)
    {
        services.AddQuarkSerialization();

        var builder = new DefaultClientBuilder(services);
        configure?.Invoke(builder);
        return services;
    }

    // Internal builder implementations ------------------------------------

    private sealed class DefaultSiloBuilder : ISiloBuilder
    {
        public DefaultSiloBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }

        public ISiloBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new()
        {
            Services.Configure(configure);
            return this;
        }
    }

    private sealed class DefaultClientBuilder : IClientBuilder
    {
        public DefaultClientBuilder(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }

        public IClientBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new()
        {
            Services.Configure(configure);
            return this;
        }
    }
}
