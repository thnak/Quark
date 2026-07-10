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

    /// <summary>
    ///     Decides and creates the IServiceScope + construction provider for a grain call: the small
    ///     Quark-only scope composed with a cached user provider for grain types that opted into
    ///     IGrainUserServiceProviderFactory, or the flat root scope otherwise. The caller disposes the
    ///     returned scope after the call/activation completes — <see cref="ConstructionServices"/> and
    ///     anything resolved from it become invalid once the scope is disposed.
    /// </summary>
    public static (IServiceScope Scope, IServiceProvider ConstructionServices) CreateCallScope(
        IServiceProvider root, GrainActivation activation)
    {
        IUserServiceProviderRegistry registry = root.GetRequiredService<IUserServiceProviderRegistry>();
        QuarkOnlyServiceProviderHolder holder = root.GetRequiredService<QuarkOnlyServiceProviderHolder>();

        if (holder.Provider is not null &&
            registry.TryGet(activation.GrainType, out IServiceProvider? userProvider) && userProvider is not null)
        {
            IServiceScope quarkOnlyScope = holder.Provider.CreateScope();
            return (quarkOnlyScope, new CompositeServiceProvider(quarkOnlyScope.ServiceProvider, userProvider));
        }

        IServiceScope scope = root.CreateScope();
        return (scope, scope.ServiceProvider);
    }
}
