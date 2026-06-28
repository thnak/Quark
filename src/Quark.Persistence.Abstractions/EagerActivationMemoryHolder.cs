using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal marker. Used by <c>GrainActivation.RunEagerInitAsync</c> to locate eager
///     holders in the memory bag and call <see cref="InitAsync"/> before <c>OnActivateAsync</c>.
/// </summary>
public interface IEagerActivationMemoryHolder
{
    bool IsInitialized { get; }
    ValueTask InitAsync(IServiceProvider scopedServices, CancellationToken ct);
}

/// <summary>
///     Engine-internal. Shell-owned holder for an <see cref="IEagerActivationMemory{T}"/> resource.
///     One instance per (GrainActivation, T). Implements <see cref="IAsyncDisposable"/> so that
///     the existing <c>DisposeManagedHoldersAsync</c> deactivation path picks it up with no extra
///     wiring in <c>GrainActivation</c>.
/// </summary>
public sealed class EagerActivationMemoryHolder<T> : IEagerActivationMemory<T>, IEagerActivationMemoryHolder, IAsyncDisposable
    where T : class
{
    private T? _value;
    private bool _initialized;
    private Func<IServiceProvider, CancellationToken, ValueTask<T>>? _factory;
    private Func<T, ValueTask>? _cleanup;

    public IEagerActivationMemory<T> Load(Func<IServiceProvider, CancellationToken, ValueTask<T>> factory)
    {
        _factory = factory;
        return this;
    }

    public IEagerActivationMemory<T> Destroy(Func<T, ValueTask> cleanup)
    {
        _cleanup = cleanup;
        return this;
    }

    public T Value
    {
        get
        {
            if (!_initialized)
                throw new InvalidOperationException(
                    $"IEagerActivationMemory<{typeof(T).Name}> has not been initialized. " +
                    "Ensure Load() is called in the behavior constructor and activation has completed.");
            return _value!;
        }
    }

    public bool IsInitialized => _initialized;

    public async ValueTask InitAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        if (_initialized) return;
        if (_factory is null)
            throw new InvalidOperationException(
                $"IEagerActivationMemory<{typeof(T).Name}>.Load() must be called in the behavior " +
                "constructor before activation.");
        _value = await _factory(scopedServices, ct).ConfigureAwait(false);
        _initialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized && _value is not null && _cleanup is not null)
            await _cleanup(_value).ConfigureAwait(false);
    }
}
