namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
///     Typed deep-copier.
/// </summary>
/// <typeparam name="T">The type that this copier handles.</typeparam>
public interface IDeepCopier<T>
{
    /// <summary>
    ///     Returns a deep copy of <paramref name="original" />.
    ///     Implementations for immutable types may return <paramref name="original" /> unchanged.
    /// </summary>
    T DeepCopy(T original, CopyContext context);
}
