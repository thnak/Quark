using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal static class GrainScopeBinder
{
    public static async ValueTask<IGrainBehavior> BindAndResolveAsync(
        IServiceProvider sp,
        GrainActivation activation,
        CancellationToken cancellationToken)
    {
        ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = activation;

        ICallContextSetter callContextSetter = sp.GetRequiredService<ICallContextSetter>();
        callContextSetter.Set(activation.GrainId);

        if (sp.GetService<IGrainScopeInitializerRegistry>() is { } registry &&
            registry.TryGet(activation.GrainType, out GrainScopeInitializer initializer))
        {
            await initializer(sp.GetRequiredService<ICallContext>(), sp, cancellationToken).ConfigureAwait(false);
        }

        return sp.GetRequiredService<IBehaviorResolver>().Resolve(activation.GrainType);
    }
}
