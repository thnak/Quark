using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Exceptions;

namespace Quark.Serialization.Providers;

/// <summary>
/// Resolves <see cref="IDeepCopier{T}"/> implementations registered via DI.
/// </summary>
public sealed class CopierProvider : ICopierProvider
{
    private readonly IServiceProvider _services;
    private readonly List<IGeneralizedCopier> _generalizedCopiers;

    /// <summary>Initialises the provider from the DI container.</summary>
    public CopierProvider(IServiceProvider services, IEnumerable<IGeneralizedCopier> generalizedCopiers)
    {
        _services = services;
        _generalizedCopiers = new List<IGeneralizedCopier>(generalizedCopiers);
    }

    /// <inheritdoc/>
    public IDeepCopier<T>? TryGetCopier<T>()
    {
        return (IDeepCopier<T>?)_services.GetService(typeof(IDeepCopier<T>));
    }

    /// <inheritdoc/>
    public IDeepCopier<T> GetRequiredCopier<T>()
    {
        return TryGetCopier<T>()
               ?? throw new SerializationException(
                   $"No copier is registered for type '{typeof(T).FullName}'. " +
                   "Annotate the type with [GenerateSerializer] or register a custom IDeepCopier<T>.");
    }

    /// <inheritdoc/>
    public IGeneralizedCopier? TryGetGeneralizedCopier(Type type)
    {
        foreach (IGeneralizedCopier copier in _generalizedCopiers)
        {
            if (copier.IsSupportedType(type))
                return copier;
        }
        return null;
    }
}
