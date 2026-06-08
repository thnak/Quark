using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Engine-internal. Used by LocalGrainCallInvoker to stamp the GrainId
///     into the scope before the behavior is constructed.
/// </summary>
public interface ICallContextSetter
{
    void Set(GrainId grainId);
}
