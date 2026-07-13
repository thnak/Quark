using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

/// <summary>
///     Per-activation (<see cref="IActivationBehavior"/>) variant of <see cref="PingPongGrainBehavior"/>,
///     used to measure the allocation delta of the activation-scoped dispatch path (one cached instance +
///     scope) versus the default per-call model. Implements the same <see cref="IPingPongGrain"/> so the
///     existing invokable dispatches to it unchanged.
/// </summary>
public sealed class ActivationPingPongGrainBehavior : IActivationBehavior, IPingPongGrain
{
    public ValueTask PingAsync() => default;
}
