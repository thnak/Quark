using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public static class InMemoryStreamingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a named in-memory stream provider.
    ///     Drop-in equivalent of Orleans' <c>AddMemoryStreams(name)</c>.
    /// </summary>
    public static IServiceCollection AddMemoryStreams(this IServiceCollection services, string providerName)
    {
        services.TryAddSingleton<StreamSubscriptionRegistry>();
        services.TryAddSingleton<IUntypedStreamSubscriptionRegistry>(
            sp => sp.GetRequiredService<StreamSubscriptionRegistry>());
        services.AddKeyedSingleton<IStreamProvider>(providerName,
            (sp, _) => new InMemoryStreamProvider(
                providerName,
                sp.GetRequiredService<StreamSubscriptionRegistry>()));
        return services;
    }
}
