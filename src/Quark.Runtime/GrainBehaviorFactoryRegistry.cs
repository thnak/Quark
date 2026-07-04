using System.Collections.Concurrent;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Maps <see cref="GrainType" /> keys to a compile-time-known factory delegate that constructs the
///     behavior instance without reflection. Populated exclusively by the generated
///     <c>QuarkRegistrations.g.cs</c> path (via <c>AddGrainBehavior&lt;,&gt;(factory: ...)</c>).
///     Behaviors registered without an explicit factory (hand-wired test/manual registrations) have no
///     entry here and fall back to <see cref="ReflectionBehaviorActivator" />.
/// </summary>
public sealed class GrainBehaviorFactoryRegistry
{
    private readonly ConcurrentDictionary<string, Func<IServiceProvider, IGrainBehavior>> _map =
        new(StringComparer.Ordinal);

    /// <summary>Registers a compile-time factory for the given grain type.</summary>
    public void Register(GrainType grainType, Func<IServiceProvider, IGrainBehavior> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _map[grainType.Value] = factory;
    }

    /// <summary>Attempts to look up the compile-time factory for the supplied grain type key.</summary>
    public bool TryGetFactory(GrainType grainType, out Func<IServiceProvider, IGrainBehavior>? factory)
    {
        return _map.TryGetValue(grainType.Value, out factory);
    }
}
