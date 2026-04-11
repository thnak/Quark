namespace Quark.Core.Abstractions.Messaging;

/// <summary>
/// Ambient context values that flow with grain-to-grain calls.
/// Values set here are automatically propagated to any grain calls made within the same logical chain.
/// </summary>
public static class RequestContext
{
    [ThreadStatic]
    private static Dictionary<string, object?>? _values;

    private static Dictionary<string, object?> Values => _values ??= new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>Sets a context value for the current call chain.</summary>
    public static void Set(string key, object? value) => Values[key] = value;

    /// <summary>Gets a context value, or <c>null</c> if not set.</summary>
    public static object? Get(string key) => Values.TryGetValue(key, out object? v) ? v : null;

    /// <summary>Removes a context value.</summary>
    public static void Remove(string key) => Values.Remove(key);

    /// <summary>Returns a snapshot of all current context values.</summary>
    public static IReadOnlyDictionary<string, object?> GetAll() =>
        _values is { Count: > 0 } d ? new Dictionary<string, object?>(d) : new Dictionary<string, object?>();

    /// <summary>Replaces all context values with those from <paramref name="values"/>.</summary>
    internal static void Import(IReadOnlyDictionary<string, object?> values)
    {
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    /// <summary>Clears all context values.</summary>
    internal static void Clear() => _values?.Clear();
}
