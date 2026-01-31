using Quark.Abstractions;

namespace Quark.Examples.ContextRegistration;

/// <summary>
/// Context class for registering external actor interfaces for proxy generation.
/// Using QuarkActorContext allows you to generate proxies for interfaces from
/// external libraries that don't inherit from IQuarkActor.
/// </summary>
[QuarkActorContext]
[QuarkActor(typeof(ICalculatorService))]
public partial class ExternalActorContext
{
}
