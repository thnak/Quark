using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quark.Queries;

/// <summary>
/// Extension methods for registering actor query services.
/// </summary>
public static class ActorQueryServiceExtensions
{
    /// <summary>
    /// Adds actor query services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddActorQueries(this IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<IActorQueryService, ActorQueryService>();

        return services;
    }
}
