namespace Quark.Abstractions.Persistence;

/// <summary>
/// Registry for state storage providers.
/// Maps storage provider names to actual storage implementations.
/// </summary>
public interface IStateStorageProvider
{
    /// <summary>
    /// Gets a state storage instance for the specified provider and state type.
    /// </summary>
    /// <typeparam name="TState">The type of state to store.</typeparam>
    /// <param name="providerName">The name of the storage provider (e.g., "sql-db", "redis-cache").</param>
    /// <returns>The state storage instance.</returns>
    IStateStorage<TState> GetStorage<TState>(string providerName) where TState : class;
}
