using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Exceptions;

namespace Quark.Serialization.Providers;

/// <summary>
/// Resolves <see cref="IFieldCodec{T}"/> implementations registered via DI.
/// Codecs are resolved once per type and cached.
/// </summary>
public sealed class CodecProvider : ICodecProvider
{
    private readonly IServiceProvider _services;
    private readonly List<IGeneralizedCodec> _generalizedCodecs;

    /// <summary>Initialises the provider from the DI container.</summary>
    public CodecProvider(IServiceProvider services, IEnumerable<IGeneralizedCodec> generalizedCodecs)
    {
        _services = services;
        _generalizedCodecs = new List<IGeneralizedCodec>(generalizedCodecs);
    }

    /// <inheritdoc/>
    public IFieldCodec<T>? TryGetCodec<T>()
    {
        return (IFieldCodec<T>?)_services.GetService(typeof(IFieldCodec<T>));
    }

    /// <inheritdoc/>
    public IFieldCodec<T> GetRequiredCodec<T>()
    {
        return TryGetCodec<T>()
               ?? throw new CodecNotFoundException(typeof(T));
    }

    /// <inheritdoc/>
    public IGeneralizedCodec? TryGetGeneralizedCodec(Type type)
    {
        foreach (IGeneralizedCodec codec in _generalizedCodecs)
        {
            if (codec.IsSupportedType(type))
                return codec;
        }
        return null;
    }
}
