using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class BehaviorResolverTests
{
    [Fact]
    public void Resolve_UsesRegisteredFactory_NeverReflection()
    {
        // Widget is deliberately NOT registered in DI. If BehaviorResolver fell back to
        // ActivatorUtilities.CreateInstance (reflection) here, resolving WidgetBehavior's
        // constructor parameter would throw. Success proves the factory path was used.
        var services = new ServiceCollection();
        var typeRegistry = new GrainTypeRegistry();
        var factoryRegistry = new GrainBehaviorFactoryRegistry();
        var grainType = new GrainType("Widget");

        factoryRegistry.Register(grainType, static _ => new WidgetBehavior(new Widget(42)));

        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(provider, typeRegistry, factoryRegistry);

        var behavior = Assert.IsType<WidgetBehavior>(resolver.Resolve(grainType));
        Assert.Equal(42, behavior.Widget.Value);
    }

    [Fact]
    public void Resolve_FallsBackToReflection_WhenNoFactoryRegistered()
    {
        var services = new ServiceCollection();
        var typeRegistry = new GrainTypeRegistry();
        var factoryRegistry = new GrainBehaviorFactoryRegistry();
        var grainType = new GrainType("PlainCounter");
        typeRegistry.Register(grainType, typeof(PlainCounterBehavior));

        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(provider, typeRegistry, factoryRegistry);

        Assert.IsType<PlainCounterBehavior>(resolver.Resolve(grainType));
    }

    [Fact]
    public void Resolve_Throws_WhenGrainTypeUnknown()
    {
        var services = new ServiceCollection();
        using ServiceProvider provider = services.BuildServiceProvider();
        var resolver = new BehaviorResolver(provider, new GrainTypeRegistry(), new GrainBehaviorFactoryRegistry());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new GrainType("Missing")));
    }

    private sealed class Widget(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WidgetBehavior(Widget widget) : IGrainBehavior
    {
        public Widget Widget { get; } = widget;
    }

    private sealed class PlainCounterBehavior : IGrainBehavior;
}
