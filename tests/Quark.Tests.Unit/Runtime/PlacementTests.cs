using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Placement;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Unit coverage for placement strategy resolution and silo selection (issue #32):
///     <see cref="AttributePlacementStrategyResolver"/> attribute mapping/precedence/caching
///     and <see cref="PlacementDirector"/> per-strategy selection, driven by a stub resolver
///     so each switch branch is exercised deterministically.
/// </summary>
public sealed class PlacementTests
{
    // =====================================================================
    // AttributePlacementStrategyResolver
    // =====================================================================

    private static PlacementStrategy Resolve<T>() =>
        new AttributePlacementStrategyResolver().GetPlacementStrategy(typeof(T));

    [Fact]
    public void NoAttribute_ResolvesToRandom()
        => Assert.Same(RandomPlacement.Singleton, Resolve<PlainGrain>());

    [Fact]
    public void PreferLocalAttribute_ResolvesToPreferLocal()
        => Assert.Same(PreferLocalPlacement.Singleton, Resolve<PreferLocalGrain>());

    [Fact]
    public void LocalAttribute_ResolvesToLocalPlacement()
        // [LocalPlacement] has distinct must-be-local semantics, separate from prefer-local.
        => Assert.Same(LocalPlacement.Singleton, Resolve<LocalGrain>());

    [Fact]
    public void PreferLocalAndLocalAttributes_AreResolvedToDistinctStrategies()
    {
        Assert.Same(PreferLocalPlacement.Singleton, Resolve<PreferLocalGrain>());
        Assert.Same(LocalPlacement.Singleton, Resolve<LocalGrain>());
    }

    [Fact]
    public void HashBasedAttribute_ResolvesToHashBased()
        => Assert.Same(HashBasedPlacement.Singleton, Resolve<HashGrain>());

    [Fact]
    public void StatelessWorkerAttribute_ResolvesToStatelessWorker_WithDefaultMax()
    {
        PlacementStrategy strategy = Resolve<StatelessGrain>();
        StatelessWorkerPlacement worker = Assert.IsType<StatelessWorkerPlacement>(strategy);
        Assert.Equal(-1, worker.MaxLocalWorkers);
    }

    [Fact]
    public void StatelessWorkerAttribute_PreservesMaxLocalWorkers()
    {
        PlacementStrategy strategy = Resolve<BoundedStatelessGrain>();
        StatelessWorkerPlacement worker = Assert.IsType<StatelessWorkerPlacement>(strategy);
        Assert.Equal(4, worker.MaxLocalWorkers);
    }

    [Fact]
    public void Resolve_CachesResult_ReturnsSameInstance()
    {
        var resolver = new AttributePlacementStrategyResolver();
        PlacementStrategy first = resolver.GetPlacementStrategy(typeof(BoundedStatelessGrain));
        PlacementStrategy second = resolver.GetPlacementStrategy(typeof(BoundedStatelessGrain));
        Assert.Same(first, second);
    }

    [Fact]
    public void PreferLocal_TakesPrecedence_OverHashAndStatelessWorker()
        => Assert.Same(PreferLocalPlacement.Singleton, Resolve<PreferLocalAndHashGrain>());

    [Fact]
    public void HashBased_TakesPrecedence_OverStatelessWorker()
        => Assert.Same(HashBasedPlacement.Singleton, Resolve<HashAndStatelessGrain>());

    [Fact]
    public void GetPlacementStrategy_NullGrainClass_Throws()
        => Assert.Throws<ArgumentNullException>(
            () => new AttributePlacementStrategyResolver().GetPlacementStrategy(null!));

    [Fact]
    public void Register_TakesPrecedence_OverAttributeReflection()
    {
        var resolver = new AttributePlacementStrategyResolver();
        // PreferLocalGrain carries [PreferLocalPlacement], but an explicit Register() call
        // (as the generator would emit) must win over the attribute-reflection fallback.
        resolver.Register(typeof(PreferLocalGrain), HashBasedPlacement.Singleton);

        Assert.Same(HashBasedPlacement.Singleton, resolver.GetPlacementStrategy(typeof(PreferLocalGrain)));
    }

    [Fact]
    public void UnregisteredType_StillFallsBackToAttributeReflection()
    {
        var resolver = new AttributePlacementStrategyResolver();
        resolver.Register(typeof(PreferLocalGrain), HashBasedPlacement.Singleton);

        // HashGrain was never Register()'d — must still resolve via attribute reflection.
        Assert.Same(HashBasedPlacement.Singleton, resolver.GetPlacementStrategy(typeof(HashGrain)));
    }

    // =====================================================================
    // AddGrainPlacementStrategy — end-to-end DI wiring (issue: silent no-op if
    // AttributePlacementStrategyResolver isn't resolvable as its own concrete singleton)
    // =====================================================================

    [Fact]
    public void AddGrainPlacementStrategy_AppliedThroughBuiltServiceProvider_ResolvesRegisteredStrategy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = "test";
            o.ServiceId = "placement-strategy-di";
            o.SiloName = "silo0";
        });
        services.AddQuarkRuntime();
        services.AddGrainPlacementStrategy<TestBehavior>(HashBasedPlacement.Singleton);

        using ServiceProvider provider = services.BuildServiceProvider();

        // Mirrors SiloHostedService.ApplyPlacementStrategyRegistrations().
        var resolver = provider.GetRequiredService<AttributePlacementStrategyResolver>();
        foreach (RuntimeServiceCollectionExtensions.IGrainPlacementStrategyRegistration registration
                 in provider.GetServices<RuntimeServiceCollectionExtensions.IGrainPlacementStrategyRegistration>())
        {
            registration.Apply(resolver);
        }

        PlacementStrategy resolved = provider.GetRequiredService<IPlacementStrategyResolver>()
            .GetPlacementStrategy(typeof(TestBehavior));

        Assert.Same(HashBasedPlacement.Singleton, resolved);
    }

    private sealed class TestBehavior : IGrainBehavior;

    // =====================================================================
    // PlacementDirector
    // =====================================================================

    private static readonly SiloAddress SiloA = new("10.0.0.1", 100);
    private static readonly SiloAddress SiloB = new("10.0.0.2", 100);
    private static readonly SiloAddress SiloC = new("10.0.0.3", 100);

    private static PlacementDirector DirectorFor(PlacementStrategy strategy)
        => new(new StubResolver(strategy));

    private static GrainId Grain(string key) => new(new GrainType("G"), key);

    [Fact]
    public void SelectActivationSilo_EmptyAvailable_Throws()
    {
        PlacementDirector director = DirectorFor(RandomPlacement.Singleton);
        Assert.Throws<InvalidOperationException>(
            () => director.SelectActivationSilo(Grain("k"), typeof(object), SiloA, []));
    }

    [Fact]
    public void SelectActivationSilo_NullGrainClass_Throws()
    {
        PlacementDirector director = DirectorFor(RandomPlacement.Singleton);
        Assert.Throws<ArgumentNullException>(
            () => director.SelectActivationSilo(Grain("k"), null!, SiloA, [SiloA]));
    }

    [Fact]
    public void SelectActivationSilo_NullAvailable_Throws()
    {
        PlacementDirector director = DirectorFor(RandomPlacement.Singleton);
        Assert.Throws<ArgumentNullException>(
            () => director.SelectActivationSilo(Grain("k"), typeof(object), SiloA, null!));
    }

    [Fact]
    public void PreferLocal_WhenLocalAvailable_ReturnsLocal()
    {
        PlacementDirector director = DirectorFor(PreferLocalPlacement.Singleton);
        SiloAddress chosen = director.SelectActivationSilo(Grain("k"), typeof(object), SiloB, [SiloA, SiloB, SiloC]);
        Assert.Equal(SiloB, chosen);
    }

    [Fact]
    public void PreferLocal_WhenLocalNotAvailable_FallsBackToAvailable()
    {
        PlacementDirector director = DirectorFor(PreferLocalPlacement.Singleton);
        // Local silo absent; single candidate makes the random fallback deterministic.
        SiloAddress chosen = director.SelectActivationSilo(Grain("k"), typeof(object), SiloC, [SiloA]);
        Assert.Equal(SiloA, chosen);
    }

    [Fact]
    public void LocalPlacement_WhenLocalAvailable_ReturnsLocal()
    {
        PlacementDirector director = DirectorFor(LocalPlacement.Singleton);
        SiloAddress chosen = director.SelectActivationSilo(Grain("k"), typeof(object), SiloA, [SiloA, SiloB]);
        Assert.Equal(SiloA, chosen);
    }

    [Fact]
    public void LocalPlacement_WhenLocalNotAvailable_Throws()
    {
        // Must-be-local refuses to fall back when the local silo is not a candidate,
        // distinguishing it from prefer-local (which falls back to random).
        PlacementDirector director = DirectorFor(LocalPlacement.Singleton);
        Assert.Throws<InvalidOperationException>(
            () => director.SelectActivationSilo(Grain("k"), typeof(object), SiloC, [SiloA, SiloB]));
    }

    [Fact]
    public void StatelessWorker_WhenLocalAvailable_ReturnsLocal()
    {
        PlacementDirector director = DirectorFor(new StatelessWorkerPlacement(4));
        SiloAddress chosen = director.SelectActivationSilo(Grain("k"), typeof(object), SiloB, [SiloA, SiloB]);
        Assert.Equal(SiloB, chosen);
    }

    [Fact]
    public void HashBased_IsDeterministic_AcrossCalls()
    {
        PlacementDirector director = DirectorFor(HashBasedPlacement.Singleton);
        GrainId grain = Grain("stable-key");
        SiloAddress[] silos = [SiloA, SiloB, SiloC];

        SiloAddress first = director.SelectActivationSilo(grain, typeof(object), SiloA, silos);
        SiloAddress second = director.SelectActivationSilo(grain, typeof(object), SiloA, silos);
        Assert.Equal(first, second);
    }

    [Fact]
    public void HashBased_IsOrderIndependent()
    {
        PlacementDirector director = DirectorFor(HashBasedPlacement.Singleton);
        GrainId grain = Grain("stable-key");

        SiloAddress fromOneOrder = director.SelectActivationSilo(grain, typeof(object), SiloA, [SiloA, SiloB, SiloC]);
        SiloAddress fromAnother = director.SelectActivationSilo(grain, typeof(object), SiloA, [SiloC, SiloA, SiloB]);
        Assert.Equal(fromOneOrder, fromAnother);
    }

    [Fact]
    public void HashBased_SelectsFromAvailable()
    {
        PlacementDirector director = DirectorFor(HashBasedPlacement.Singleton);
        SiloAddress[] silos = [SiloA, SiloB, SiloC];
        SiloAddress chosen = director.SelectActivationSilo(Grain("any"), typeof(object), SiloA, silos);
        Assert.Contains(chosen, silos);
    }

    [Fact]
    public void HashBased_DistinctKeysCanMapToDistinctSilos()
    {
        PlacementDirector director = DirectorFor(HashBasedPlacement.Singleton);
        SiloAddress[] silos = [SiloA, SiloB, SiloC];
        var chosen = new HashSet<SiloAddress>();
        for (int i = 0; i < 50; i++)
        {
            chosen.Add(director.SelectActivationSilo(Grain($"key-{i}"), typeof(object), SiloA, silos));
        }

        // Hashing across many keys should spread over more than one silo.
        Assert.True(chosen.Count > 1, $"Expected spread across silos, got {chosen.Count}.");
    }

    [Fact]
    public void Random_WithSingleSilo_ReturnsThatSilo()
    {
        PlacementDirector director = DirectorFor(RandomPlacement.Singleton);
        SiloAddress chosen = director.SelectActivationSilo(Grain("k"), typeof(object), SiloA, [SiloB]);
        Assert.Equal(SiloB, chosen);
    }

    [Fact]
    public void Random_AlwaysSelectsFromAvailable()
    {
        PlacementDirector director = DirectorFor(RandomPlacement.Singleton);
        SiloAddress[] silos = [SiloA, SiloB, SiloC];
        for (int i = 0; i < 50; i++)
        {
            SiloAddress chosen = director.SelectActivationSilo(Grain($"k{i}"), typeof(object), SiloA, silos);
            Assert.Contains(chosen, silos);
        }
    }

    // =====================================================================
    // Test fixtures
    // =====================================================================

    private sealed class StubResolver : IPlacementStrategyResolver
    {
        private readonly PlacementStrategy _strategy;
        public StubResolver(PlacementStrategy strategy) => _strategy = strategy;
        public PlacementStrategy GetPlacementStrategy(Type grainClass) => _strategy;
    }

    private sealed class PlainGrain;

    [PreferLocalPlacement]
    private sealed class PreferLocalGrain;

    [LocalPlacement]
    private sealed class LocalGrain;

    [HashBasedPlacement]
    private sealed class HashGrain;

    [StatelessWorker]
    private sealed class StatelessGrain;

    [StatelessWorker(4)]
    private sealed class BoundedStatelessGrain;

    [PreferLocalPlacement]
    [HashBasedPlacement]
    [StatelessWorker]
    private sealed class PreferLocalAndHashGrain;

    [HashBasedPlacement]
    [StatelessWorker]
    private sealed class HashAndStatelessGrain;
}
