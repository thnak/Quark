using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

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

    /// <summary>
    ///     Returns a proxy for the given pre-built <see cref="GrainId" />.
    ///     Used by <c>AsReference&lt;T&gt;()</c> to obtain a self-reference inside a grain.
    /// </summary>
    TGrainInterface GetGrain<TGrainInterface>(GrainId grainId)
        where TGrainInterface : IGrain
        => throw new NotSupportedException(
            $"GetGrain(GrainId) is not supported by {GetType().Name}.");

    /// <summary>
    ///     Wraps a local <see cref="IGrainObserver" /> implementation in a proxy so that
    ///     other grains can call it by reference.
    ///     Drop-in equivalent of Orleans' <c>IGrainFactory.CreateObjectReference&lt;T&gt;</c>.
    /// </summary>
    TGrainObserver CreateObjectReference<TGrainObserver>(TGrainObserver implementation)
        where TGrainObserver : class, IGrainObserver
        => throw new NotSupportedException(
            $"CreateObjectReference is not supported by {GetType().Name}. " +
            "Register observer support via AddObserverProxy<> and AddObserverMethodInvoker<>.");

    /// <summary>
    ///     Unregisters an observer proxy created by <see cref="CreateObjectReference{T}" />.
    ///     Call when the observer is no longer needed to free the registry entry.
    ///     Drop-in equivalent of Orleans' <c>IGrainFactory.DeleteObjectReference&lt;T&gt;</c>.
    /// </summary>
    void DeleteObjectReference<TGrainObserver>(TGrainObserver reference)
        where TGrainObserver : class, IGrainObserver
    {
        // Default no-op — concrete implementations that track observer registrations override this.
    }

    /// <summary>
    ///     Reconstructs an observer proxy for an already-registered observer <see cref="GrainId" />.
    ///     Used by the transport dispatcher to turn a wire-encoded observer GrainId back into a
    ///     callable proxy without re-registering the local implementation.
    /// </summary>
    TGrainObserver GetObserverRef<TGrainObserver>(GrainId grainId)
        where TGrainObserver : class, IGrainObserver
        => throw new NotSupportedException(
            $"GetObserverRef is not supported by {GetType().Name}. " +
            "Register observer proxy support via AddObserverProxy<>.");
}
