namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Marks a grain class as reentrant, allowing concurrent interleaved execution
///     while an awaited call is in progress.
///     Drop-in equivalent of Orleans' <c>[Reentrant]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ReentrantAttribute : Attribute
{
}
