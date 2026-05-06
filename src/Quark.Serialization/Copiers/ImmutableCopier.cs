using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Serialization.Copiers;

/// <summary>
/// Immutable/value-type copier — returns the original reference (or value) unchanged.
/// Suitable for primitives, strings, and other well-known immutable types.
/// </summary>
/// <typeparam name="T">An immutable or value type.</typeparam>
public sealed class ImmutableCopier<T> : IDeepCopier<T>
{
    /// <inheritdoc/>
    public T DeepCopy(T original, CopyContext context) => original;
}
