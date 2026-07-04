namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
///     Deep-copy interface — returns a detached copy of the object graph.
/// </summary>
public interface IDeepCopyable// TODO did not implemented or used in any elsewhere
{
    /// <summary>Returns a deep copy of <paramref name="value" />.</summary>
    T DeepCopy<T>(T value);
}
