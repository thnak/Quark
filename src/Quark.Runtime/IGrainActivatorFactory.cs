using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     AOT-safe factory for constructing a specific grain class without runtime reflection.
///     Generated factories implement this interface and can be registered with DI.
/// </summary>
public interface IGrainActivatorFactory
{
    /// <summary>The concrete grain class handled by this factory.</summary>
    Type GrainClass { get; }

    /// <summary>Creates a grain instance for <paramref name="grainId" /> using services from <paramref name="services" />.</summary>
    Grain Create(GrainId grainId, IServiceProvider services);
}
