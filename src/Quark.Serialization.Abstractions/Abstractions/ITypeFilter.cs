namespace Quark.Serialization.Abstractions;

/// <summary>
/// Filter used by the serializer infrastructure to decide which types are handled
/// by a given codec or copier.  Implement this on the same class as your codec/copier
/// to narrow the types it claims to support.
/// </summary>
public interface ITypeFilter
{
    /// <summary>
    /// Returns <c>true</c> if this implementation is willing to handle <paramref name="type"/>.
    /// </summary>
    bool IsTypeAllowed(Type type);
}
