using Microsoft.Extensions.DependencyInjection;
using Quark.Hosting;

namespace Quark.Extensions.DependencyInjection;

/// <summary>
/// Builder for configuring Quark Silo services.
/// </summary>
public interface IQuarkSiloBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the silo options.
    /// </summary>
    QuarkSiloOptions Options { get; }
}