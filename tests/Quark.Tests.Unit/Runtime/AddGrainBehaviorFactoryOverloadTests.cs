using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class AddGrainBehaviorFactoryOverloadTests
{
    [Fact]
    public void AddGrainBehavior_WithExplicitBehaviorIdAndFactory_RegistersBothWithoutReflection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "factory-overload";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();

        // Widget is deliberately never registered in DI.
        services.AddGrainBehavior<IWidgetGrain, WidgetBehavior>(
            behaviorId: "custom-widget-id",
            factory: static _ => new WidgetBehavior(new Widget(7)));

        using ServiceProvider provider = services.BuildServiceProvider();

        var typeRegistry = provider.GetRequiredService<GrainTypeRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorRegistration>())
        {
            reg.Apply(typeRegistry);
        }

        var factoryRegistry = provider.GetRequiredService<GrainBehaviorFactoryRegistry>();
        foreach (RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration reg in
                 provider.GetServices<RuntimeServiceCollectionExtensions.IGrainBehaviorFactoryRegistration>())
        {
            reg.Apply(factoryRegistry);
        }

        var expectedGrainType = new GrainType("custom-widget-id");
        Assert.True(typeRegistry.TryGetGrainClass(expectedGrainType, out Type? clrType));
        Assert.Equal(typeof(WidgetBehavior), clrType);

        Assert.True(factoryRegistry.TryGetFactory(expectedGrainType, out var factory));
        var behavior = Assert.IsType<WidgetBehavior>(factory!(provider));
        Assert.Equal(7, behavior.Widget.Value);
    }

    private interface IWidgetGrain : IGrain
    {
    }

    private sealed class Widget(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WidgetBehavior(Widget widget) : IGrainBehavior, IWidgetGrain
    {
        public Widget Widget { get; } = widget;
    }
}
