namespace Quark.Serialization.Abstractions;

/// <summary>
/// Core contract for a deep-copy provider that can handle any type it recognises.
/// Immutable types should return the original reference; mutable types must return a fresh deep copy.
/// </summary>
public interface IGeneralizedCopier
{
    /// <summary>Returns <c>true</c> if this copier can deep-copy <paramref name="type"/>.</summary>
    bool IsSupportedType(Type type);

    /// <summary>Returns a deep copy of <paramref name="original"/>.</summary>
    object? DeepCopy(object? original, CopyContext context);
}
