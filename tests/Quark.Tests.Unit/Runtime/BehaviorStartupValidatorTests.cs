using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class BehaviorStartupValidatorTests
{
    [Fact]
    public async Task StartAsync_ValidatesViaFactory_NeverReflection()
    {
        // Widget is deliberately NOT registered in DI — proves the factory path is used,
        // not ActivatorUtilities.CreateInstance (which would throw resolving it reflectively).
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "startup-validator";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();

        var grainType = new GrainType("Widget");
        var typeRegistry = new GrainTypeRegistry();
        typeRegistry.Register(grainType, typeof(WidgetBehavior));
        services.AddSingleton(typeRegistry);
        services.AddSingleton<IGrainTypeRegistry>(typeRegistry);

        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<GrainBehaviorFactoryRegistry>()
            .Register(grainType, static _ => new WidgetBehavior(new Widget(42)));

        var validator = new BehaviorStartupValidator(
            typeRegistry,
            provider,
            NullLogger<BehaviorStartupValidator>.Instance,
            provider.GetRequiredService<GrainBehaviorFactoryRegistry>());

        await validator.StartAsync(CancellationToken.None);
        // No exception => validation succeeded via the factory, without touching DI reflectively.
    }

    private sealed class Widget(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class WidgetBehavior(Widget widget) : IGrainBehavior
    {
        public Widget Widget { get; } = widget;
    }
}
