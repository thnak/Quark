namespace Quark.Core.Abstractions;

/// <summary>
/// Marker for grain observer callbacks (one-way, no return value from observer).
/// </summary>
public interface IGrainObserver : IAddressable
{
}
