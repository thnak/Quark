using Quark.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Context class for registering external actor interfaces for proxy generation.
/// This demonstrates the new context-based registration approach.
/// </summary>
[QuarkActorContext]
[QuarkActor(typeof(IExternalLibraryActor))]
public partial class TestActorContext
{
}
