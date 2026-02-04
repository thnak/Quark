using Quark.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Test interface for dispatcher registration.
/// </summary>
public interface IInterfaceTestActor : IQuarkActor
{
    Task<string> TestMethodAsync();
}