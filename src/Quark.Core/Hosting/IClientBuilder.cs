using Microsoft.Extensions.DependencyInjection;

namespace Quark.Core.Hosting;

/// <summary>
///     Builder interface for configuring a Quark cluster client.
/// </summary>
public interface IClientBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Configures client options.</summary>
    IClientBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new();
}
