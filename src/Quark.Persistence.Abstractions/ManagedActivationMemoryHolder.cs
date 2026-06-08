using Quark.Core.Abstractions.Hosting;

namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. Shell-owned holder for an <see cref="IManagedActivationMemory{T}"/> resource.
///     One instance per (GrainActivation, T). Implements <see cref="IAsyncDisposable"/> so that
///     the grain's deactivation sequence automatically invokes the configured destroy delegate.
/// </summary>
public sealed class ManagedActivationMemoryHolder<T> : IManagedActivationMemory<T>, IAsyncDisposable
    where T : class
{
    private T? _value;
    private bool _initialized;
    private Func<Task<T>>? _factory;
    private Func<T, Task>? _cleanup;

    public IManagedActivationMemory<T> Init(Func<Task<T>> factory)
    {
        _factory = factory;
        return this;
    }

    public IManagedActivationMemory<T> Destroy(Func<T, Task> cleanup)
    {
        _cleanup = cleanup;
        return this;
    }

    public async ValueTask<T> GetAsync(CancellationToken ct = default)
    {
        if (_initialized) return _value!;
        if (_factory is null)
            throw new InvalidOperationException(
                $"IManagedActivationMemory<{typeof(T).Name}>.Init() must be configured before first access.");
        _value = await _factory().ConfigureAwait(false);
        _initialized = true;
        return _value;
    }

    public bool IsInitialized => _initialized;

    public async ValueTask DisposeAsync()
    {
        if (_initialized && _value is not null && _cleanup is not null)
            await _cleanup(_value).ConfigureAwait(false);
    }
}
