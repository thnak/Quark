namespace Quark.Transport.Abstractions;

/// <summary>
/// Key/value headers attached to a <see cref="MessageEnvelope"/>.
/// All keys and values are strings to keep encoding trivial and AOT-safe.
/// </summary>
public sealed class MessageHeaders
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Sets a header value.</summary>
    public void Set(string key, string value) => _values[key] = value;

    /// <summary>Gets a header value, or <c>null</c> if not present.</summary>
    public string? Get(string key) => _values.TryGetValue(key, out string? v) ? v : null;

    /// <summary>All headers as a read-only view.</summary>
    public IReadOnlyDictionary<string, string> All => _values;
}