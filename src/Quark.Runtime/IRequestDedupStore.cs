namespace Quark.Runtime;

/// <summary>
///     Bounded, self-evicting store that gives a keyed grain call at-most-once execution within a
///     configurable window. Consulted only at the terminal executing silo's dispatch checkpoint.
///     Registered as a singleton; per-grain sub-tables are evicted on grain deactivation.
/// </summary>
public interface IRequestDedupStore
{
    /// <summary>
    ///     Begins (or joins) a keyed call.
    ///     Returns a lease describing whether the caller must execute, may replay a recorded outcome,
    ///     or must reject the request due to an argument-hash conflict.
    ///     <para>
    ///         Concurrent duplicates (same key arriving while the first is still in-flight) await the
    ///         in-flight Task inside this method and return <see cref="DedupOutcome.Replay" /> once
    ///         the original caller calls <see cref="Complete" />.
    ///     </para>
    /// </summary>
    ValueTask<DedupLease> TryBeginAsync(
        GrainId grainId,
        string idempotencyKey,
        ulong argHash,
        CancellationToken ct = default);

    /// <summary>
    ///     Records the terminal outcome (success or failure bytes) for a lease that returned
    ///     <see cref="DedupOutcome.Execute" />. Must always be called, even on failure, so that
    ///     concurrent waiters are unblocked and future retries receive a replay.
    /// </summary>
    void Complete(GrainId grainId, string idempotencyKey, ReadOnlyMemory<byte> responsePayload);

    /// <summary>
    ///     Drops all dedup entries for the specified grain.
    ///     Called from the grain deactivation callback so idle-collection prunes the store automatically.
    /// </summary>
    void EvictGrain(GrainId grainId);
}