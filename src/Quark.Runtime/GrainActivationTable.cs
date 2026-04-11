using Quark.Core.Abstractions;
using System.Collections.Concurrent;

namespace Quark.Runtime;

/// <summary>
/// Tracks all live grain activations on the local silo.
/// Thread-safe; uses lazy-init to ensure only one activation is created per grain even
/// under concurrent call pressure.
/// </summary>
public sealed class GrainActivationTable : IAsyncDisposable
{
    // Lazy<Task<>> pattern: two concurrent callers both get the same Lazy; only one
    // actually runs the factory; both await the same Task.
    private readonly ConcurrentDictionary<GrainId, Lazy<Task<GrainActivation>>> _activations = new();

    /// <summary>
    /// Returns the existing activation for <paramref name="grainId"/>, or creates one using
    /// <paramref name="factory"/>.  The factory is only invoked once per grain, even under
    /// concurrent access.
    /// </summary>
    public Task<GrainActivation> GetOrCreateAsync(GrainId grainId, Func<Task<GrainActivation>> factory)
    {
        return _activations.GetOrAdd(grainId,
            static (_, f) => new Lazy<Task<GrainActivation>>(f),
            factory).Value;
    }

    /// <summary>
    /// Attempts to retrieve an already-running activation without creating one.
    /// </summary>
    public bool TryGetActivation(GrainId grainId, out GrainActivation? activation)
    {
        if (_activations.TryGetValue(grainId, out var lazy) && lazy.IsValueCreated)
        {
            var task = lazy.Value;
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
    /// Deactivates and removes the grain activation for <paramref name="grainId"/>.
    /// No-op if the grain is not currently active.
    /// </summary>
    public async Task TryDeactivateAsync(GrainId grainId)
    {
        if (_activations.TryRemove(grainId, out var lazy) && lazy.IsValueCreated)
        {
            try
            {
                var activation = await lazy.Value.ConfigureAwait(false);
                await activation.DisposeAsync().ConfigureAwait(false);
            }
            catch { /* already disposed or failed to create */ }
        }
    }

    /// <summary>Total number of currently tracked activations (including pending).</summary>
    public int Count => _activations.Count;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in _activations.Values)
        {
            if (!lazy.IsValueCreated) continue;
            try
            {
                var activation = await lazy.Value.ConfigureAwait(false);
                await activation.DisposeAsync().ConfigureAwait(false);
            }
            catch { }
        }
        _activations.Clear();
    }
}
