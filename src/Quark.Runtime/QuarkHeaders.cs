namespace Quark.Runtime;

/// <summary>
///     Well-known header names stamped on <c>MessageEnvelope.Headers</c> by the Quark runtime.
/// </summary>
public static class QuarkHeaders
{
    /// <summary>Silo-to-silo hop marker; presence routes to the terminal invoker to prevent loops.</summary>
    public const string Hop = "x-quark-hop";

    /// <summary>Caller-supplied idempotency key; enables at-most-once execution at the terminal silo.</summary>
    public const string IdempotencyKey = "x-quark-idem";

    /// <summary>Transaction marker; presence causes the dedup checkpoint to be skipped.</summary>
    public const string Transaction = "x-quark-tx";
}
