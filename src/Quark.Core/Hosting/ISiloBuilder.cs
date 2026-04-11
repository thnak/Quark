using Microsoft.Extensions.DependencyInjection;

namespace Quark.Core.Hosting;

/// <summary>
/// Builder interface for configuring a Quark silo.
/// </summary>
public interface ISiloBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures silo options.
    /// </summary>
    ISiloBuilder Configure<TOptions>(Action<TOptions> configure) where TOptions : class, new();
}
