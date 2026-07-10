using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class UserServiceProviderFactoryTests
{
    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsFalse_WhenNotRegistered()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.False(registry.TryGet(new GrainType("Unregistered"), out _));
    }

    [Fact]
    public void UserServiceProviderRegistry_TryGet_ReturnsRegisteredProvider()
    {
        var registry = new UserServiceProviderRegistry();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        var grainType = new GrainType("Widget");

        registry.Register(grainType, provider);

        Assert.True(registry.TryGet(grainType, out IServiceProvider? found));
        Assert.Same(provider, found);
    }

    [Fact]
    public void UserServiceProviderRegistry_Register_Throws_OnNullProvider()
    {
        var registry = new UserServiceProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(new GrainType("Widget"), null!));
    }

    [Fact]
    public void QuarkOnlyServiceProviderHolder_DefaultsToNull()
    {
        Assert.Null(new QuarkOnlyServiceProviderHolder().Provider);
    }
}
