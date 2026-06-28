namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Provides activation-scoped access to a resource that is initialized once, eagerly,
///     before <c>OnActivateAsync</c> runs. The factory receives a scoped
///     <see cref="IServiceProvider"/> so it can resolve DI services. Access after initialization
///     is synchronous via <see cref="Value"/>. NOT persisted to storage.
/// </summary>
/// <remarks>
///     Fills the gap between <see cref="IActivationMemory{TState}"/> (default-constructed, no DI,
///     sync) and <see cref="IManagedActivationMemory{T}"/> (lazy async, no DI in factory).
///     Lifecycle: factory runs inside the activation scope after the behavior constructor fires
///     (which registers the factory via <see cref="Load"/>), but before <c>OnActivateAsync</c>.
///     The <see cref="Destroy"/> callback runs after <c>OnDeactivateAsync</c> completes.
///     Thread-safety: do not access from timer callbacks or external threads without routing
///     through <c>GrainActivation.PostAsync</c>.
/// </remarks>
public interface IEagerActivationMemory<T> where T : class
{
    /// <summary>
    ///     Configures the async factory used to initialize the resource at activation time.
    ///     The factory receives the scoped <see cref="IServiceProvider"/> from the activation
    ///     scope and may resolve any service registered in DI.
    ///     Must be called in the behavior constructor.
    /// </summary>
    IEagerActivationMemory<T> Load(Func<IServiceProvider, CancellationToken, ValueTask<T>> factory);

    /// <summary>
    ///     Configures the async callback invoked to clean up the resource on grain deactivation.
    ///     Called after the grain's <c>OnDeactivateAsync</c> completes. Optional.
    /// </summary>
    IEagerActivationMemory<T> Destroy(Func<T, ValueTask> cleanup);

    /// <summary>
    ///     The initialized resource value. Available synchronously from within
    ///     <c>OnActivateAsync</c> and all subsequent grain calls.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if accessed before activation completes or if <see cref="Load"/> was never called.
    /// </exception>
    T Value { get; }

    /// <summary>True if the resource has been initialized.</summary>
    bool IsInitialized { get; }
}
