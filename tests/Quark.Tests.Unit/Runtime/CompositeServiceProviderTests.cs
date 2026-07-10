using Microsoft.Extensions.DependencyInjection;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class CompositeServiceProviderTests
{
    private interface IMarkerA;
    private interface IMarkerB;

    private sealed class MarkerA : IMarkerA;
    private sealed class MarkerB : IMarkerB;

    [Fact]
    public void GetService_ReturnsFromPrimary_WhenPrimaryHasIt()
    {
        var primaryServices = new ServiceCollection();
        primaryServices.AddSingleton<IMarkerA, MarkerA>();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.IsType<MarkerA>(composite.GetService(typeof(IMarkerA)));
    }

    [Fact]
    public void GetService_FallsBackToSecondary_WhenPrimaryDoesNotHaveIt()
    {
        var primaryServices = new ServiceCollection();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        secondaryServices.AddSingleton<IMarkerB, MarkerB>();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.IsType<MarkerB>(composite.GetService(typeof(IMarkerB)));
    }

    [Fact]
    public void GetService_ReturnsNull_WhenNeitherHasIt()
    {
        var primaryServices = new ServiceCollection();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.Null(composite.GetService(typeof(IMarkerA)));
    }

    [Fact]
    public void GetService_PrimaryWins_WhenBothHaveIt()
    {
        var primaryServices = new ServiceCollection();
        primaryServices.AddSingleton<IMarkerA, MarkerA>();
        using ServiceProvider primary = primaryServices.BuildServiceProvider();

        var secondaryServices = new ServiceCollection();
        var secondaryMarker = new MarkerA();
        secondaryServices.AddSingleton<IMarkerA>(secondaryMarker);
        using ServiceProvider secondary = secondaryServices.BuildServiceProvider();

        var composite = new CompositeServiceProvider(primary, secondary);

        Assert.NotSame(secondaryMarker, composite.GetService(typeof(IMarkerA)));
    }
}
