using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Tracks all live grain activations on the local silo.
///     Thread-safe; uses lazy-init to ensure only one activation is created per grain even
///     under concurrent call pressure.
/// </summary>
public sealed class GrainActivationTable : IAsyncDisposable
{
    // Lazy<Task<>> pattern: two concurrent callers both get the same Lazy; only one
    // actually runs the factory; both await the same Task.
    //
    // The cached value is a Task<GrainActivation>, not a ValueTask<GrainActivation> — a
    // ValueTask is documented as unsafe to consume from more than one awaiter (its backing
    // IValueTaskSource can only be awaited once), so caching one here and handing the same
    // struct instance to every concurrent caller of an already-active grain is a latent
    // correctness bug: two callers racing to await the same cached ValueTask can leave one of
    // them suspended forever. Task natively supports any number of concurrent awaiters, so
    // GetOrCreateAsync converts the caller's ValueTask to a Task once (via AsTask()) before
    // caching, then wraps a *fresh* ValueTask around the shared Task for every call.
    private readonly ConcurrentDictionary<GrainId, Lazy<Task<GrainActivation>>> _activations = new();
    private readonly ILogger<GrainActivationTable> _logger;
    private readonly int _maxActivations;

    public GrainActivationTable(
        ILogger<GrainActivationTable> logger,
        IOptions<SiloRuntimeOptions>? options = null)
    {
        _logger = logger;
        _maxActivations = options?.Value.MaxActivations ?? 0;
    }

    /// <summary>Total number of currently tracked activations (including pending).</summary>
    public int Count => _activations.Count;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<Task<GrainActivation>> lazy in _activations.Values)
        {
            if (!lazy.IsValueCreated)
            {
                continue;
            }

            try
            {
                GrainActivation activation = await lazy.Value.ConfigureAwait(false);
                await activation.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing grain activation during silo shutdown.");
            }
        }

        _activations.Clear();
    }

    /// <summary>
    ///     Returns the existing activation for <paramref name="grainId" />, or creates one using
    ///     <paramref name="factory" />.  The factory is only invoked once per grain, even under
    ///     concurrent access.
    /// </summary>
    public ValueTask<GrainActivation> GetOrCreateAsync(GrainId grainId, Func<ValueTask<GrainActivation>> factory)
    {
        // Existing grains are always reachable — only the creation of *new* activations is capped.
        if (_activations.TryGetValue(grainId, out Lazy<Task<GrainActivation>>? existing))
        {
            return new ValueTask<GrainActivation>(existing.Value);
        }

        // Best-effort cap: the count check races with concurrent adds, so the live total may briefly
        // exceed the cap by the number of in-flight creators, but it cannot grow without bound.
        if (_maxActivations > 0 && _activations.Count >= _maxActivations)
        {
            throw new GrainActivationLimitExceededException(grainId, _maxActivations);
        }

        Lazy<Task<GrainActivation>> lazy = _activations.GetOrAdd(grainId,
            static (_, f) => new Lazy<Task<GrainActivation>>(() => f().AsTask()),
            factory);
        return new ValueTask<GrainActivation>(lazy.Value);
    }

    /// <summary>
    ///     State-passing overload of <see cref="GetOrCreateAsync(GrainId, Func{ValueTask{GrainActivation}})"/>
    ///     that avoids the per-call factory-closure allocation on the hot (cache-hit) path — the caller
    ///     passes a <c>static</c> delegate plus a struct <paramref name="state"/> instead of a lambda that
    ///     captures its arguments. Dedup semantics are identical: the same <see cref="Lazy{T}"/> guarantees
    ///     the factory runs at most once per grain. The closure over <paramref name="state"/>/
    ///     <paramref name="factory"/> that the <see cref="Lazy{T}"/> needs is created only on a cache miss.
    /// </summary>
    public ValueTask<GrainActivation> GetOrCreateAsync<TState>(
        GrainId grainId, TState state, Func<TState, ValueTask<GrainActivation>> factory)
    {
        if (_activations.TryGetValue(grainId, out Lazy<Task<GrainActivation>>? existing))
        {
            return new ValueTask<GrainActivation>(existing.Value);
        }

        if (_maxActivations > 0 && _activations.Count >= _maxActivations)
        {
            throw new GrainActivationLimitExceededException(grainId, _maxActivations);
        }

        Lazy<Task<GrainActivation>> lazy = _activations.GetOrAdd(grainId,
            static (_, arg) => new Lazy<Task<GrainActivation>>(() => arg.factory(arg.state).AsTask()),
            (state, factory));
        return new ValueTask<GrainActivation>(lazy.Value);
    }

    /// <summary>
    ///     Removes the activation entry for <paramref name="grainId" /> if it is currently
    ///     faulted.  Called by the invoker after a failed activation so that the next call
    ///     can attempt a fresh activation.
    /// </summary>
    public void RemoveIfFaulted(GrainId grainId)
    {
        if (_activations.TryGetValue(grainId, out Lazy<Task<GrainActivation>>? lazy)
            && lazy is { IsValueCreated: true, Value.IsFaulted: true })
        {
            _activations.TryRemove(new KeyValuePair<GrainId, Lazy<Task<GrainActivation>>>(grainId, lazy));
        }
    }

    /// <summary>
    ///     Returns a snapshot of all currently active (fully-started, non-deactivating) activations.
    ///     Called by <see cref="GrainIdleCollector"/> each collection cycle.
    /// </summary>
    public IReadOnlyList<(GrainId GrainId, GrainActivation Activation)> GetActiveActivations()
    {
        var result = new List<(GrainId, GrainActivation)>();
        foreach (var (grainId, lazy) in _activations)
        {
            if (!lazy.IsValueCreated || !lazy.Value.IsCompletedSuccessfully)
            {
                continue;
            }

            GrainActivation activation = lazy.Value.Result;
            if (activation.ActivationStatus == GrainActivationStatus.Active)
            {
                result.Add((grainId, activation));
            }
        }
        return result;
    }

    /// <summary>
    ///     Attempts to retrieve an already-running activation without creating one.
    /// </summary>
    public bool TryGetActivation(GrainId grainId, out GrainActivation? activation)
    {
        if (_activations.TryGetValue(grainId, out Lazy<Task<GrainActivation>>? lazy) && lazy.IsValueCreated)
        {
            Task<GrainActivation> task = lazy.Value;
            if (task.IsCompletedSuccessfully)
            {
                activation = task.Result;
                return true;
            }
        }

        activation = null;
        return false;
    }

    /// <summary>
    ///     Removes the activation entry for <paramref name="grainId" /> without disposing it.
    ///     Called by <see cref="GrainActivation" /> after its own deactivation sequence completes,
    ///     so only the table entry needs to be cleared.
    /// </summary>
    public void Remove(GrainId grainId) => _activations.TryRemove(grainId, out _);

    /// <summary>
    ///     Deactivates and removes the grain activation for <paramref name="grainId" />.
    ///     Runs <c>OnDeactivateAsync</c> on the grain's scheduler before tearing down the loop.
    ///     No-op if the grain is not currently tracked.
    /// </summary>
    public async Task TryDeactivateAsync(GrainId grainId) // TODO did not called anywhere
    {
        if (_activations.TryRemove(grainId, out Lazy<Task<GrainActivation>>? lazy) && lazy.IsValueCreated)
        {
            try
            {
                GrainActivation activation = await lazy.Value.ConfigureAwait(false);
                await activation.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating grain {GrainId}.", grainId);
            }
        }
    }
}
