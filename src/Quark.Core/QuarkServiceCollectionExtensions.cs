using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Hosting;
using Quark.Serialization;

namespace Quark.Core;

/// <summary>
///     Extension methods for registering Quark services into an <see cref="IServiceCollection" />.
/// </summary>
public static class QuarkServiceCollectionExtensions
{
    /// <summary>
    ///     Adds core Quark silo services to <paramref name="services" />.
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
    ///     Adds core Quark client services to <paramref name="services" />.
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

    private sealed class DefaultSiloBuilder(IServiceCollection services) : ISiloBuilder
    {
        public IServiceCollection Services { get; } = services;

        public ISiloBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new()
        {
            Services.Configure(configure);
            return this;
        }
    }

    private sealed class DefaultClientBuilder(IServiceCollection services) : IClientBuilder
    {
        public IServiceCollection Services { get; } = services;

        public IClientBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new()
        {
            Services.Configure(configure);
            return this;
        }
    }
}
