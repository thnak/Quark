using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainBehaviorFactoryRegistryTests
{
    [Fact]
    public void TryGetFactory_ReturnsFalse_WhenNotRegistered()
    {
        var registry = new GrainBehaviorFactoryRegistry();
        Assert.False(registry.TryGetFactory(new GrainType("Unknown"), out _));
    }

    [Fact]
    public void Register_ThenTryGetFactory_ReturnsRegisteredFactory()
    {
        var registry = new GrainBehaviorFactoryRegistry();
        var grainType = new GrainType("Widget");
        IGrainBehavior expected = new StubBehavior();

        registry.Register(grainType, _ => expected);

        Assert.True(registry.TryGetFactory(grainType, out var factory));
        Assert.Same(expected, factory!(NullServiceProvider.Instance));
    }

    private sealed class StubBehavior : IGrainBehavior;

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
