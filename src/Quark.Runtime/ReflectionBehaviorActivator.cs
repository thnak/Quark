using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;

namespace Quark.Runtime;

/// <summary>
///     Reflection-based construction fallback for grain behaviors registered without an explicit
///     compile-time factory — i.e. hand-wired <c>AddGrainBehavior&lt;,&gt;()</c> calls that don't pass the
///     <c>factory</c> parameter, as used by manual test/sample wiring (see <c>quark-testing</c> skill).
///     Behaviors registered via the generated <c>QuarkRegistrations.g.cs</c> path always supply a factory
///     and never reach this method.
/// </summary>
internal static class ReflectionBehaviorActivator
{
    [RequiresUnreferencedCode(
        "Reflection-based behavior construction fallback for hand-wired (non-generator) " +
        "AddGrainBehavior<,>() registrations. Behaviors registered through the generated " +
        "QuarkRegistrations.g.cs path always supply a compile-time factory and never call this.")]
    public static IGrainBehavior Create(IServiceProvider scope, Type behaviorType)
    {
        return (IGrainBehavior)ActivatorUtilities.CreateInstance(scope, behaviorType);
    }
}
