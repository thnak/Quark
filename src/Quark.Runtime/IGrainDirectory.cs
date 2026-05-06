using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
/// Stores the location of grain activations (which silo hosts each grain).
/// </summary>
public interface IGrainDirectory
{
    /// <summary>
    /// Registers the activation of <paramref name="grainId"/> on <paramref name="siloAddress"/>.
    /// Returns <c>false</c> if another silo already owns it (use <paramref name="existing"/>).
    /// </summary>
    bool TryRegister(GrainId grainId, SiloAddress siloAddress, out SiloAddress existing);

    /// <summary>
    /// Removes the registration for <paramref name="grainId"/> if it matches <paramref name="siloAddress"/>.
    /// </summary>
    bool TryUnregister(GrainId grainId, SiloAddress siloAddress);

    /// <summary>
    /// Looks up the current activation address for <paramref name="grainId"/>.
    /// </summary>
    bool TryLookup(GrainId grainId, out SiloAddress siloAddress);
}