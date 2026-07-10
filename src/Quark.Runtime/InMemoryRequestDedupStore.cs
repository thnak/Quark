using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Quark.Runtime;

/// <summary>
///     Default per-activation in-memory implementation of <see cref="IRequestDedupStore" />.
///     Maintains a <see cref="PerGrainDedupTable" /> per grain; entries are count-capped, TTL-checked,
///     and evicted when the grain deactivates via <see cref="EvictGrain" />.
/// </summary>
public sealed class InMemoryRequestDedupStore : IRequestDedupStore
{
    private readonly ConcurrentDictionary<GrainId, PerGrainDedupTable> _tables = new();
    private readonly int _maxEntriesPerGrain;
    private readonly TimeSpan _window;

    public InMemoryRequestDedupStore(IOptions<IdempotencyOptions> options)
    {
        IdempotencyOptions opts = options.Value;
        _maxEntriesPerGrain = opts.MaxEntriesPerGrain;
        _window = opts.Window;
    }

    public ValueTask<DedupLease> TryBeginAsync(
        GrainId grainId, string idempotencyKey, ulong argHash, CancellationToken ct = default)
    {
        PerGrainDedupTable table = _tables.GetOrAdd(
            grainId,
            static (_, ctx) => new PerGrainDedupTable(ctx.max, ctx.window),
            (max: _maxEntriesPerGrain, window: _window));

        return table.TryBeginAsync(idempotencyKey, argHash, ct);
    }

    public Task CompleteAsync(
        GrainId grainId, string idempotencyKey, ReadOnlyMemory<byte> responsePayload, CancellationToken ct = default)
    {
        if (_tables.TryGetValue(grainId, out PerGrainDedupTable? table))
            table.Complete(idempotencyKey, responsePayload);
        return Task.CompletedTask;
    }

    public void EvictGrain(GrainId grainId) => _tables.TryRemove(grainId, out _);

    // -----------------------------------------------------------------------

    private sealed class PerGrainDedupTable(int maxEntries, TimeSpan window)
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, DedupEntry> _entries = new(StringComparer.Ordinal);

        public async ValueTask<DedupLease> TryBeginAsync(string key, ulong argHash, CancellationToken ct)
        {
            Task<ReadOnlyMemory<byte>>? inFlight;
            DedupLease syncLease;

            lock (_lock)
            {
                (syncLease, inFlight) = TryBeginLocked(key, argHash);
            }

            if (inFlight is null)
                return syncLease;

            // Concurrent duplicate: await the in-flight task outside the lock.
            ReadOnlyMemory<byte> payload = await inFlight.WaitAsync(ct).ConfigureAwait(false);
            return new DedupLease(DedupOutcome.Replay, payload);
        }

        private (DedupLease lease, Task<ReadOnlyMemory<byte>>? inFlight) TryBeginLocked(string key, ulong argHash)
        {
            if (_entries.TryGetValue(key, out DedupEntry? existing))
            {
                if (DateTimeOffset.UtcNow - existing.CreatedAt > window)
                {
                    _entries.Remove(key);
                    // Expired — fall through to treat as MISS.
                }
                else if (existing.ArgHash != argHash)
                {
                    return (new DedupLease(DedupOutcome.Conflict, ReadOnlyMemory<byte>.Empty), null);
                }
                else if (existing.IsCompleted)
                {
                    return (new DedupLease(DedupOutcome.Replay, existing.CompletedPayload), null);
                }
                else
                {
                    return (default, existing.InFlightTask);
                }
            }

            if (_entries.Count >= maxEntries)
                EvictOldest();

            var entry = new DedupEntry(argHash);
            _entries[key] = entry;
            return (new DedupLease(DedupOutcome.Execute, ReadOnlyMemory<byte>.Empty), null);
        }

        private void EvictOldest()
        {
            // Prefer evicting the oldest completed entry; fall back to oldest overall.
            string? victim = null;
            DateTimeOffset oldest = DateTimeOffset.MaxValue;

            foreach ((string k, DedupEntry e) in _entries)
            {
                if (e.IsCompleted && e.CreatedAt < oldest)
                {
                    oldest = e.CreatedAt;
                    victim = k;
                }
            }

            if (victim is null)
            {
                oldest = DateTimeOffset.MaxValue;
                foreach ((string k, DedupEntry e) in _entries)
                {
                    if (e.CreatedAt < oldest)
                    {
                        oldest = e.CreatedAt;
                        victim = k;
                    }
                }
            }

            if (victim is not null)
                _entries.Remove(victim);
        }

        public void Complete(string key, ReadOnlyMemory<byte> payload)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out DedupEntry? entry))
                    entry.Complete(payload);
            }
        }
    }

    // -----------------------------------------------------------------------

    private sealed class DedupEntry(ulong argHash)
    {
        private readonly TaskCompletionSource<ReadOnlyMemory<byte>> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ulong ArgHash { get; } = argHash;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public bool IsCompleted => _tcs.Task.IsCompleted;
        public ReadOnlyMemory<byte> CompletedPayload => _tcs.Task.Result;
        public Task<ReadOnlyMemory<byte>> InFlightTask => _tcs.Task;

        public void Complete(ReadOnlyMemory<byte> payload) => _tcs.TrySetResult(payload);
    }
}
