using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Engine-internal. Used by the dispatch path to stamp per-call identity
///     into the scope before the behavior is constructed.
/// </summary>
public interface ICallContextSetter
{
    void Set(GrainId grainId);

    void SetIdempotencyKey(string? key);
}
