using System.Runtime.CompilerServices;
using Quark.Generated;

namespace Quark.Tests;

/// <summary>
/// Module initializer to register all actor proxy factories for the test assembly.
/// </summary>
internal static class TestModuleInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Register all actor proxy factories generated for this assembly
        QuarkTestsActorProxyFactoryRegistration.RegisterAll();
    }
}
