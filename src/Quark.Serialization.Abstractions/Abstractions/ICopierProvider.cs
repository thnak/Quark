namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
/// Resolves <see cref="IDeepCopier{T}"/> instances and <see cref="IGeneralizedCopier"/>s
/// for arbitrary types.
/// </summary>
public interface ICopierProvider
{
    /// <summary>Returns a typed deep-copier for <typeparamref name="T"/>, or <c>null</c>.</summary>
    IDeepCopier<T>? TryGetCopier<T>();

    /// <summary>Returns a typed deep-copier for <typeparamref name="T"/>.</summary>
    /// <exception cref="Exceptions.SerializationException">Thrown when no copier is registered.</exception>
    IDeepCopier<T> GetRequiredCopier<T>();

    /// <summary>Returns a generalized copier for <paramref name="type"/>, or <c>null</c>.</summary>
    IGeneralizedCopier? TryGetGeneralizedCopier(Type type);
}
