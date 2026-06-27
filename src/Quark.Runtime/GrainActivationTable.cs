using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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
    private readonly ConcurrentDictionary<GrainId, Lazy<Task<GrainActivation>>> _activations = new();
    private readonly ILogger<GrainActivationTable> _logger;

    public GrainActivationTable(ILogger<GrainActivationTable> logger)
    {
        _logger = logger;
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
    public Task<GrainActivation> GetOrCreateAsync(GrainId grainId, Func<Task<GrainActivation>> factory)
    {
        return _activations.GetOrAdd(grainId,
            static (_, f) => new Lazy<Task<GrainActivation>>(f),
            factory).Value;
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
