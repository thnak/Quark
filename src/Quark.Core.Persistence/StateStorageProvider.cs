using System.Collections.Concurrent;
using Quark.Abstractions.Persistence;

namespace Quark.Core.Persistence;

/// <summary>
/// Default implementation of state storage provider.
/// Manages multiple storage providers and routes requests to the appropriate storage.
/// </summary>
public class StateStorageProvider : IStateStorageProvider
{
    private readonly ConcurrentDictionary<(string ProviderName, Type StateType), object> _storages = new();
    private readonly Dictionary<string, Func<Type, object>> _storageFactories = new();

    /// <summary>
    /// Registers a storage factory for a specific provider name.
    /// </summary>
    /// <param name="providerName">The name of the storage provider.</param>
    /// <param name="factory">Factory function that creates storage instances.</param>
    public void RegisterStorage(string providerName, Func<Type, object> factory)
    {
        _storageFactories[providerName] = factory;
    }

    /// <inheritdoc />
    public IStateStorage<TState> GetStorage<TState>(string providerName) where TState : class
    {
        var key = (providerName, typeof(TState));
        
        return (IStateStorage<TState>)_storages.GetOrAdd(key, _ =>
        {
            if (_storageFactories.TryGetValue(providerName, out var factory))
            {
                return factory(typeof(TState));
            }

            // Default to in-memory storage
            return new InMemoryStateStorage<TState>();
        });
    }
}
