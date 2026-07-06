namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
///     Context passed to deep-copy operations.
///     Tracks already-copied object references to handle cyclic graphs.
/// </summary>
public sealed class CopyContext
{
    private Dictionary<object, object>? _copies;

    /// <summary>Records that <paramref name="original" /> was copied to <paramref name="copy" />.</summary>
    public void RecordCopy(object original, object copy)
    {
        (_copies ??= new Dictionary<object, object>(ReferenceEqualityComparer.Instance)).TryAdd(original, copy);
    }

    /// <summary>
    ///     Returns a previously recorded copy of <paramref name="original" />, or <c>null</c>
    ///     if the object has not been copied yet.
    /// </summary>
    public T? TryGetCopy<T>(object original) where T : class
    {
        if (_copies is null) return null;
        return _copies.TryGetValue(original, out object? copy) ? (T)copy : null;
    }

    /// <summary>Resets the context for reuse.</summary>
    public void Reset()
    {
        _copies?.Clear();
    }
}
