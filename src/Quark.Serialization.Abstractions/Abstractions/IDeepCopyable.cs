namespace Quark.Serialization.Abstractions;

/// <summary>
/// Deep-copy interface — returns a detached copy of the object graph.
/// </summary>
public interface IDeepCopyable
{
    /// <summary>Returns a deep copy of <paramref name="value"/>.</summary>
    T DeepCopy<T>(T value);
}