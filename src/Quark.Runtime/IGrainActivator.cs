using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Creates grain instances from a registered grain identity.
/// </summary>
public interface IGrainActivator
{
    /// <summary>
    ///     Creates (but does not activate) a grain instance for the given <paramref name="grainId" />.
    /// </summary>
    Grain CreateInstance(GrainId grainId);
}
