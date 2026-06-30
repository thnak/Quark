using System.Collections.Concurrent;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

internal sealed class GrainScopeInitializerRegistry : IGrainScopeInitializerRegistry
{
    private readonly ConcurrentDictionary<GrainType, GrainScopeInitializer> _initializers = new();

    public void Register(GrainType grainType, GrainScopeInitializer initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        _initializers[grainType] = initializer;
    }

    public bool TryGet(GrainType grainType, out GrainScopeInitializer initializer)
        => _initializers.TryGetValue(grainType, out initializer!);
}
