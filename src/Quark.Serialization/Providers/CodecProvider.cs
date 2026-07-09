using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Exceptions;

namespace Quark.Serialization.Providers;

/// <summary>
///     Resolves <see cref="IFieldCodec{T}" /> implementations registered via DI.
///     Codecs are resolved once per type and cached.
/// </summary>
public sealed class CodecProvider : ICodecProvider
{
    // Resolved lazily on first use to break the circular dependency:
    //   ICodecProvider → IEnumerable<IGeneralizedCodec> → *UserCodec → ICodecProvider
    private List<IGeneralizedCodec>? _generalizedCodecs;
    private readonly IServiceProvider _services;

    /// <summary>Initialises the provider from the DI container.</summary>
    public CodecProvider(IServiceProvider services)
    {
        _services = services;
    }

    /// <inheritdoc />
    public IFieldCodec<T>? TryGetCodec<T>()
    {
        return _services.GetService<IFieldCodec<T>>();
    }

    /// <inheritdoc />
    public IFieldCodec<T> GetRequiredCodec<T>()
    {
        return TryGetCodec<T>()
               ?? throw new CodecNotFoundException(typeof(T));
    }

    /// <inheritdoc />
    public IGeneralizedCodec? TryGetGeneralizedCodec(Type type)
    {
        List<IGeneralizedCodec> generalizedCodecs = LazyInitializer.EnsureInitialized(
            ref _generalizedCodecs,
            () => new List<IGeneralizedCodec>(_services.GetServices<IGeneralizedCodec>()));
        foreach (IGeneralizedCodec codec in generalizedCodecs)
        {
            if (codec.IsSupportedType(type))
            {
                return codec;
            }
        }

        return null;
    }
}
