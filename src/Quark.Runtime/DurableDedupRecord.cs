namespace Quark.Runtime;

/// <summary>
///     Durable-tier dedup record persisted per <c>(GrainId, idempotencyKey)</c> via
///     <see cref="Quark.Persistence.Abstractions.IGrainStorage" />.
///     Only ever written once a call completes (write-before-ack) — a stored record therefore
///     always represents a completed outcome, never an in-flight one.
/// </summary>
public sealed class DurableDedupRecord
{
    /// <summary>Argument hash recorded at completion; guards against key reuse with different arguments.</summary>
    public ulong ArgHash { get; set; }

    /// <summary>The serialized <c>GrainInvocationResponse</c> bytes to replay for this key.</summary>
    public byte[]? Payload { get; set; }

    /// <summary>UTC ticks when this record was written; compared against <c>IdempotencyOptions.Window</c>.</summary>
    public long CreatedAtUtcTicks { get; set; }
}
