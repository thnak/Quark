namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Marker for grain observer callbacks (one-way, no return value from observer).
/// </summary>
public interface IGrainObserver : IAddressable
{
}
