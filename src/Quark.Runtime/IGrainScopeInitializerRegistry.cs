using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

internal interface IGrainScopeInitializerRegistry
{
    void Register(GrainType grainType, GrainScopeInitializer initializer);

    bool TryGet(GrainType grainType, out GrainScopeInitializer initializer);
}
