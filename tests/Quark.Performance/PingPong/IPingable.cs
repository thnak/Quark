namespace Quark.Performance.PingPong;

/// <summary>
///     Shared call surface for both the non-reentrant and <c>[Reentrant]</c> ping-pong grain
///     variants, so <see cref="PingPongRunner.RunPairAsync"/> can drive either without duplication.
/// </summary>
public interface IPingable
{
    ValueTask PingAsync();
}
