namespace Quark.Streaming.Abstractions;

/// <summary>
///     Carries a single published stream item through fan-out so observers that serialize the item
///     (e.g. gateway/TCP subscribers) encode it at most once and share the resulting bytes.
/// </summary>
public sealed class SharedStreamItem
{
    private ReadOnlyMemory<byte>? _encoded;

    /// <summary>Creates a holder for a single published <paramref name="item" />.</summary>
    public SharedStreamItem(object item) => Item = item;

    /// <summary>The raw published item, for observers that consume it without serialization.</summary>
    public object Item { get; }

    /// <summary>
    ///     Returns the encoded bytes, invoking <paramref name="encode" /> only on the first call and
    ///     memoizing the result for subsequent callers on the same instance.
    /// </summary>
    /// <remarks>
    ///     Fan-out invokes each observer's callback synchronously up to its first <c>await</c>, and
    ///     encoding runs in that synchronous prefix, so this executes single-threaded — no lock is
    ///     required. Even if that invariant were broken, the worst case is a redundant, deterministic
    ///     re-encode producing identical bytes; correctness is unaffected.
    /// </remarks>
    public ReadOnlyMemory<byte> GetOrEncode(Func<object, ReadOnlyMemory<byte>> encode)
        => _encoded ??= encode(Item);
}
