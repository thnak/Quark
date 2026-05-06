using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

/// <summary>
/// AOT-safe factory for constructing a specific grain class without runtime reflection.
/// Generated factories implement this interface and can be registered with DI.
/// </summary>
public interface IGrainActivatorFactory
{
    /// <summary>The concrete grain class handled by this factory.</summary>
    Type GrainClass { get; }

    /// <summary>Creates a grain instance using services from <paramref name="services"/>.</summary>
    Grain Create(IServiceProvider services);
}