using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainActivatorTests
{
    [Fact]
    public void DefaultGrainActivator_UsesRegisteredGeneratedFactory()
    {
        ServiceCollection services = new();
        services.AddQuarkRuntime();
        services.AddSingleton<TestDependency>();
        services.AddGrainActivatorFactory<GeneratedOnlyGrainActivatorFactory>();

        using ServiceProvider provider = services.BuildServiceProvider();

        GrainTypeRegistry registry = provider.GetRequiredService<GrainTypeRegistry>();
        registry.Register(new GrainType(nameof(GeneratedOnlyGrain)), typeof(GeneratedOnlyGrain));

        IGrainActivator activator = provider.GetRequiredService<IGrainActivator>();
        Grain instance = activator.CreateInstance(new GrainType(nameof(GeneratedOnlyGrain)));

        GeneratedOnlyGrain grain = Assert.IsType<GeneratedOnlyGrain>(instance);
        Assert.Same(provider.GetRequiredService<TestDependency>(), grain.Dependency);
    }

    public sealed class TestDependency;

    public sealed class GeneratedOnlyGrain : Grain
    {
        public GeneratedOnlyGrain(TestDependency dependency)
        {
            Dependency = dependency;
        }

        public TestDependency Dependency { get; }
    }

    public sealed class GeneratedOnlyGrainActivatorFactory : IGrainActivatorFactory
    {
        public Type GrainClass => typeof(GeneratedOnlyGrain);

        public Grain Create(IServiceProvider services)
        {
            return new GeneratedOnlyGrain(services.GetRequiredService<TestDependency>());
        }
    }
}
