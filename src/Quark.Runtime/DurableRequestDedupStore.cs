using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Quark.Persistence.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     <c>IGrainStorage</c>-backed <see cref="IRequestDedupStore" /> for the opt-in durable tier
///     (<see cref="IdempotencyOptions.Durability" /> = <see cref="DedupDurability.Durable" />).
///     Persists each completed dedup entry as its own storage record keyed by
///     <c>(GrainId, idempotencyKey)</c> — written in <see cref="CompleteAsync" />, before the caller
///     acks — so a replay survives grain deactivation and silo restart, unlike
///     <see cref="InMemoryRequestDedupStore" /> whose entries are lost on deactivation.
///     <para>
///         Concurrent duplicates arriving at the <em>same</em> activation are still deduped via an
///         in-memory in-flight table, same as the in-memory tier. A duplicate for a brand-new key
///         landing on a <em>different</em> concurrent activation of the same grain (only possible
///         under <c>[Reentrant]</c> or <c>[StatelessWorker]</c> — a normal grain's mailbox serializes
///         calls) can race the storage read/claim and both execute; this mirrors the crash-gap
///         limitation the idempotency design spec documents rather than hides.
///     </para>
/// </summary>
public sealed class DurableRequestDedupStore : IRequestDedupStore
{
    private readonly ConcurrentDictionary<GrainId, PerGrainLocalTable> _localTables = new();
    private readonly IGrainStorage _storage;
    private readonly TimeSpan _window;

    /// <summary>Initializes the durable dedup store over the named <see cref="IGrainStorage" /> provider.</summary>
    public DurableRequestDedupStore(IGrainStorage storage, IOptions<IdempotencyOptions> options)
    {
        _storage = storage;
        _window = options.Value.Window;
    }

    /// <inheritdoc />
    public async ValueTask<DedupLease> TryBeginAsync(
        GrainId grainId, string idempotencyKey, ulong argHash, CancellationToken ct = default)
    {
        PerGrainLocalTable local = _localTables.GetOrAdd(grainId, static _ => new PerGrainLocalTable());

        (DedupLease? fastLease, Task<ReadOnlyMemory<byte>>? inFlight) = local.TryFastPath(idempotencyKey, argHash);
        if (fastLease is { } lease)
            return lease;

        if (inFlight is not null)
        {
            ReadOnlyMemory<byte> payload = await inFlight.WaitAsync(ct).ConfigureAwait(false);
            return new DedupLease(DedupOutcome.Replay, payload);
        }

        var state = new GrainState<DurableDedupRecord>();
        await _storage.ReadStateAsync(StateName(idempotencyKey), grainId, state, ct).ConfigureAwait(false);

        if (state.RecordExists && DateTimeOffset.UtcNow.Ticks - state.State.CreatedAtUtcTicks <= _window.Ticks)
        {
            DurableDedupRecord record = state.State;
            return record.ArgHash != argHash
                ? new DedupLease(DedupOutcome.Conflict, ReadOnlyMemory<byte>.Empty)
                : new DedupLease(DedupOutcome.Replay, record.Payload ?? ReadOnlyMemory<byte>.Empty);
        }

        local.BeginInFlight(idempotencyKey, argHash);
        return new DedupLease(DedupOutcome.Execute, ReadOnlyMemory<byte>.Empty);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        GrainId grainId, string idempotencyKey, ReadOnlyMemory<byte> responsePayload, CancellationToken ct = default)
    {
        if (!_localTables.TryGetValue(grainId, out PerGrainLocalTable? local)
            || !local.TryCompleteInFlight(idempotencyKey, responsePayload, out ulong argHash))
        {
            return;
        }

        var state = new GrainState<DurableDedupRecord>
        {
            State = new DurableDedupRecord
            {
                ArgHash = argHash,
                Payload = responsePayload.ToArray(),
                CreatedAtUtcTicks = DateTimeOffset.UtcNow.Ticks
            }
        };
        await _storage.WriteStateAsync(StateName(idempotencyKey), grainId, state, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void EvictGrain(GrainId grainId) => _localTables.TryRemove(grainId, out _);

    private static string StateName(string idempotencyKey) => $"__quark_idem__{idempotencyKey}";

    // -----------------------------------------------------------------------

    private sealed class PerGrainLocalTable
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, LocalEntry> _entries = new(StringComparer.Ordinal);

        public (DedupLease? lease, Task<ReadOnlyMemory<byte>>? inFlight) TryFastPath(string key, ulong argHash)
        {
            lock (_lock)
            {
                if (!_entries.TryGetValue(key, out LocalEntry? existing))
                    return (null, null);

                if (existing.ArgHash != argHash)
                    return (new DedupLease(DedupOutcome.Conflict, ReadOnlyMemory<byte>.Empty), null);

                return existing.IsCompleted
                    ? (new DedupLease(DedupOutcome.Replay, existing.CompletedPayload), null)
                    : ((DedupLease?)null, existing.InFlightTask);
            }
        }

        public void BeginInFlight(string key, ulong argHash)
        {
            lock (_lock)
            {
                _entries[key] = new LocalEntry(argHash);
            }
        }

        public bool TryCompleteInFlight(string key, ReadOnlyMemory<byte> payload, out ulong argHash)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(key, out LocalEntry? entry))
                {
                    entry.Complete(payload);
                    argHash = entry.ArgHash;
                    return true;
                }
            }

            argHash = 0;
            return false;
        }
    }

    private sealed class LocalEntry(ulong argHash)
    {
        private readonly TaskCompletionSource<ReadOnlyMemory<byte>> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ulong ArgHash { get; } = argHash;
        public bool IsCompleted => _tcs.Task.IsCompleted;
        public ReadOnlyMemory<byte> CompletedPayload => _tcs.Task.Result;
        public Task<ReadOnlyMemory<byte>> InFlightTask => _tcs.Task;

        public void Complete(ReadOnlyMemory<byte> payload) => _tcs.TrySetResult(payload);
    }
}
