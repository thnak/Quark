namespace Quark.Runtime;

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