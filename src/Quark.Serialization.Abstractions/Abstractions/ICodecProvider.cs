namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
/// Resolves <see cref="IFieldCodec{T}"/> instances and <see cref="IGeneralizedCodec"/>s
/// for arbitrary types.
/// </summary>
public interface ICodecProvider
{
    /// <summary>Returns a typed codec for <typeparamref name="T"/>, or <c>null</c> if none is registered.</summary>
    IFieldCodec<T>? TryGetCodec<T>();

    /// <summary>Returns a typed codec for <typeparamref name="T"/>.</summary>
    /// <exception cref="Exceptions.SerializationException">Thrown when no codec is registered.</exception>
    IFieldCodec<T> GetRequiredCodec<T>();

    /// <summary>Returns a generalized codec for <paramref name="type"/>, or <c>null</c>.</summary>
    IGeneralizedCodec? TryGetGeneralizedCodec(Type type);
}
