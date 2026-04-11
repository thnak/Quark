namespace Quark.Core.Abstractions;

/// <summary>
/// Base marker interface for all grains.
/// Implement a more specific key interface (e.g. <see cref="IGrainWithStringKey"/>)
/// on your grain interface.
/// </summary>
public interface IGrain : IAddressable
{
}
