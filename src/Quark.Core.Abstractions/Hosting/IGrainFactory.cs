using Quark.Core.Abstractions.Grains;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Factory for obtaining grain references from an <see cref="IClusterClient" /> or from inside a grain.
/// </summary>
public interface IGrainFactory
{
    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and string key.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(string key)
        where TGrainInterface : IGrainWithStringKey;

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and integer key.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(long key)
        where TGrainInterface : IGrainWithIntegerKey;

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and <see cref="Guid" /> key.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(Guid key)
        where TGrainInterface : IGrainWithGuidKey;

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and compound integer key.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(long key, string? keyExtension)
        where TGrainInterface : IGrainWithIntegerCompoundKey;

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and compound <see cref="Guid" /> key.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(Guid key, string? keyExtension)
        where TGrainInterface : IGrainWithGuidCompoundKey;

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and string key (non-generic overload).
    ///     Drop-in equivalent of Orleans' <c>IGrainFactory.GetGrain(Type, string)</c>.
    /// </summary>
    IGrain GetGrain(Type grainInterfaceType, string key);

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and <see cref="Guid" /> key (non-generic
    ///     overload).
    /// </summary>
    IGrain GetGrain(Type grainInterfaceType, Guid key);

    /// <summary>
    ///     Returns a reference to the grain with the specified interface type and integer key (non-generic overload).
    /// </summary>
    IGrain GetGrain(Type grainInterfaceType, long key);
}
