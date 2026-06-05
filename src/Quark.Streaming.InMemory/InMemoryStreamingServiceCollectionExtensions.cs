using Microsoft.Extensions.DependencyInjection;
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
        services.AddKeyedSingleton<IStreamProvider>(providerName,
            (_, _) => new InMemoryStreamProvider(providerName));
        return services;
    }
}
