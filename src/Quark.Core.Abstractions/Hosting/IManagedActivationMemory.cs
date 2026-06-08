namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Provides activation-scoped access to a lazily-initialized resource that persists for the
///     lifetime of the grain activation. The resource is created on first access via an async
///     factory and optionally cleaned up when the grain deactivates. NOT persisted to storage.
/// </summary>
/// <remarks>
///     This fills the gap between <see cref="IActivationMemory{TState}"/> (default-constructed,
///     sync) and <see cref="Quark.Persistence.Abstractions.IPersistentActivationMemory{TState}"/>
///     (storage-backed). Use when the resource requires async initialization (e.g., pooled buffers,
///     open channels, cached projections) but must not survive deactivation.
///     Thread-safety: <see cref="GetAsync"/> is safe only from within the grain's mailbox.
///     Do not access from timer callbacks or external threads without routing through
///     <c>GrainActivation.PostAsync</c>.
/// </remarks>
public interface IManagedActivationMemory<T> where T : class
{
    /// <summary>
    ///     Configures the async factory used to create the resource on first access.
    ///     Must be called before <see cref="GetAsync"/> is first invoked.
    /// </summary>
    IManagedActivationMemory<T> Init(Func<Task<T>> factory);

    /// <summary>
    ///     Configures the async callback invoked to clean up the resource on grain deactivation.
    ///     Called after the grain's <c>OnDeactivateAsync</c> completes.
    ///     Optional: omit if the resource requires no explicit cleanup.
    /// </summary>
    IManagedActivationMemory<T> Destroy(Func<T, Task> cleanup);

    /// <summary>
    ///     Returns the resource value, initializing it on first access via the factory
    ///     configured by <see cref="Init"/>. Subsequent calls return the cached value without
    ///     invoking the factory again.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Init"/> was never called.</exception>
    ValueTask<T> GetAsync(CancellationToken ct = default);

    /// <summary>True if the resource has been initialized at least once.</summary>
    bool IsInitialized { get; }
}
