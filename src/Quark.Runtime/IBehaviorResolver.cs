using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Scoped service that resolves the <see cref="IGrainBehavior" /> for the current call.
///     Reads the behavior class from <see cref="IGrainTypeRegistry" /> and constructs it
///     via <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities" />.
/// </summary>
public interface IBehaviorResolver
{
    IGrainBehavior Resolve(GrainType grainType);
}
