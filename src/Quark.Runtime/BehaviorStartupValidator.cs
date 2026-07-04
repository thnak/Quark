using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Validates that every registered behavior's constructor dependencies are satisfied
///     at silo startup. Aborts the silo if any behavior fails DI resolution.
///     Registered as a mandatory <see cref="IHostedService" /> by <c>AddQuarkRuntime()</c>.
/// </summary>
internal sealed class BehaviorStartupValidator(
    IGrainTypeRegistry typeRegistry,
    IServiceProvider root,
    ILogger<BehaviorStartupValidator> logger,
    GrainBehaviorFactoryRegistry factoryRegistry) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        foreach ((GrainType grainType, Type behaviorType) in typeRegistry.GetAll())
        {
            try
            {
                using IServiceScope scope = root.CreateScope();
                IServiceProvider sp = scope.ServiceProvider;

                var probeId = GrainId.Create(grainType, "startup-validation-probe");
                ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>())
                    .Shell = GrainActivation.CreateProbe(probeId, grainType, root);
                sp.GetRequiredService<ICallContextSetter>().Set(probeId);

                if (factoryRegistry.TryGetFactory(grainType, out Func<IServiceProvider, IGrainBehavior>? factory) &&
                    factory is not null)
                {
                    factory(sp);
                }
                else
                {
#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) behavior registrations.
                    ReflectionBehaviorActivator.Create(sp, behaviorType);
#pragma warning restore IL2026
                }

                logger.LogDebug("Behavior {Type} DI validated", behaviorType.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Silo startup aborted: behavior '{behaviorType.FullName}' failed DI validation. " +
                    "Ensure all constructor dependencies are registered in the silo's DI container.",
                    ex);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
