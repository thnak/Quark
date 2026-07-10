using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

/// <summary>
///     Resolves the <see cref="IGrainBehavior" /> for a grain type, constructing it against an
///     explicitly-supplied <see cref="IServiceProvider" /> rather than an ambient one — so the caller
///     always controls which provider builds the behavior (the flat per-call scope by default, or a
///     composite of a Quark-only scope + a cached user provider for opted-in grain types).
/// </summary>
public interface IBehaviorResolver
{
    IGrainBehavior Resolve(GrainType grainType, IServiceProvider services);
}
