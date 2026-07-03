namespace Quark.Runtime;

/// <summary>
///     Configuration for the idempotency-key dedup mechanism registered via
///     <c>AddIdempotentCalls()</c>.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>
    ///     How long a dedup entry is retained before it expires and the key may re-execute.
    ///     Must exceed the client's maximum retry timeout; defaults to 5 minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Maximum number of dedup entries kept per grain activation (LRU-capped).
    ///     When the cap is reached, the oldest completed entry is evicted first.
    ///     Defaults to 64.
    /// </summary>
    public int MaxEntriesPerGrain { get; set; } = 64;

    /// <summary>
    ///     Selects the durability tier for the dedup store.
    ///     <see cref="DedupDurability.InMemory" /> (default) is free and correct within the activation
    ///     lifetime; <see cref="DedupDurability.Durable" /> survives deactivation at the cost of
    ///     a storage write on every keyed call.
    /// </summary>
    public DedupDurability Durability { get; set; } = DedupDurability.InMemory;

    /// <summary>
    ///     Named <c>IGrainStorage</c> provider to use when <see cref="Durability" /> is
    ///     <see cref="DedupDurability.Durable" />. Required in that tier; ignored otherwise.
    /// </summary>
    public string? DurableProviderName { get; set; }
}

/// <summary>Durability tier for the idempotency dedup store.</summary>
public enum DedupDurability
{
    /// <summary>Per-activation in-memory store; entries are lost when the grain deactivates.</summary>
    InMemory,

    /// <summary>
    ///     <c>IGrainStorage</c>-backed store; entries survive deactivation and silo restarts
    ///     (modulo the side-effect/record crash gap documented in the idempotency spec).
    /// </summary>
    Durable,
}
