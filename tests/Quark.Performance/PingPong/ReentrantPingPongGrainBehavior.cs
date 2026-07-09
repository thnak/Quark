using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.PingPong;

/// <summary>
///     Identical zero-work behavior to <see cref="PingPongGrainBehavior"/>, but <c>[Reentrant]</c> —
///     <see cref="Quark.Runtime.GrainActivation.PostAsync(System.Func{ValueTask})"/> calls the work item
///     directly for reentrant activations, bypassing the mailbox channel and its forced-async completion
///     signal entirely. Used to measure the inline/synchronous fast path Quark supports but the default
///     (non-reentrant) <see cref="PingPongGrainBehavior"/> does not exercise.
/// </summary>
[Reentrant]
public sealed class ReentrantPingPongGrainBehavior : IGrainBehavior, IReentrantPingPongGrain
{
    public ValueTask PingAsync() => default;
}
