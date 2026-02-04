using Microsoft.Extensions.DependencyInjection;

namespace Quark.Client.DependencyInjection;

internal sealed class ClusterClientBuilder : IClusterClientBuilder
{
    public ClusterClientBuilder(IServiceCollection services, ClusterClientOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public ClusterClientOptions Options { get; }
}