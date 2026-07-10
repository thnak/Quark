using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

internal sealed class UserServiceProviderRegistry : IUserServiceProviderRegistry
{
    private readonly ConcurrentDictionary<GrainType, IServiceProvider> _providers = new();

    public void Register(GrainType grainType, IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[grainType] = provider;
    }

    public bool TryGet(GrainType grainType, out IServiceProvider? provider)
        => _providers.TryGetValue(grainType, out provider);
}
