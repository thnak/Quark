using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Creates grain instances from a registered grain type.
/// </summary>
public interface IGrainActivator
{
    /// <summary>
    ///     Creates (but does not activate) a grain instance for the given <paramref name="grainType" />.
    /// </summary>
    Grain CreateInstance(GrainType grainType);
}
