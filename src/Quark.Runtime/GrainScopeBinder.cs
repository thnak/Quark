using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal static class GrainScopeBinder
{
    /// <param name="bindingServices">
    ///     Provider used to bind the shell accessor and call context — always Quark's own scope
    ///     (the flat scope by default, or the small Quark-only scope for opted-in grain types).
    /// </param>
    /// <param name="constructionServices">
    ///     Provider used to construct the behavior instance — the same as <paramref name="bindingServices"/>
    ///     by default, or a composite of the Quark-only scope + a cached user provider for opted-in
    ///     grain types.
    /// </param>
    public static IGrainBehavior BindAndResolve(
        IServiceProvider bindingServices,
        IServiceProvider constructionServices,
        GrainActivation activation)
    {
        ((ActivationShellAccessor)bindingServices.GetRequiredService<IActivationShellAccessor>()).Shell = activation;

        ICallContextSetter callContextSetter = bindingServices.GetRequiredService<ICallContextSetter>();
        callContextSetter.Set(activation.GrainId);
        callContextSetter.SetIdempotencyKey(QuarkRequestContext.IdempotencyKey);

        return bindingServices.GetRequiredService<IBehaviorResolver>().Resolve(activation.GrainType, constructionServices);
    }
}
