using Microsoft.Extensions.DependencyInjection;
using Quark.Hosting;

namespace Quark.Extensions.DependencyInjection;

internal sealed class QuarkSiloBuilder : IQuarkSiloBuilder
{
    public QuarkSiloBuilder(IServiceCollection services, QuarkSiloOptions options)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IServiceCollection Services { get; }
    public QuarkSiloOptions Options { get; }
}