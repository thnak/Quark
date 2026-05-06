using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Maps a <see cref="GrainType" /> key to the concrete CLR <see cref="Type" /> that implements
///     that grain.  Used by the activator to resolve which class to create.
/// </summary>
public interface IGrainTypeRegistry
{
    /// <summary>
    ///     Attempts to look up the CLR type for the supplied grain type key.
    /// </summary>
    bool TryGetGrainClass(GrainType grainType, out Type? grainClass);

    /// <summary>
    ///     Returns all registered (grainType, clrType) pairs.
    /// </summary>
    IEnumerable<(GrainType GrainType, Type GrainClass)> GetAll();
}
