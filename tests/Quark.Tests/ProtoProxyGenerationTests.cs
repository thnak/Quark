using Quark.Abstractions;
using Quark.Client;
using Quark.Core.Actors;
using Quark.Networking.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for the ProtoSourceGenerator - verifies that actor proxies are generated correctly.
/// </summary>
public class ProtoProxyGenerationTests
{
    /// <summary>
    /// Simple test actor for proxy generation testing.
    /// </summary>
    [Actor(Name = "TestProxy")]
    public class TestProxyActor : ActorBase
    {
        public TestProxyActor(string actorId) : base(actorId)
        {
        }

        public async Task<int> AddAsync(int a, int b)
        {
            await Task.CompletedTask;
            return a + b;
        }

        public async Task<string> GetMessageAsync()
        {
            await Task.CompletedTask;
            return "Hello from TestProxyActor";
        }

        public async Task VoidMethodAsync()
        {
            await Task.CompletedTask;
        }
    }

    [Fact]
    public void ProxyInterface_IsGenerated()
    {
        // Verify that the ITestProxyActorProxy interface type exists
        var interfaceType = Type.GetType("Quark.Tests.Generated.ITestProxyActorProxy, Quark.Tests");
        Assert.NotNull(interfaceType);
        Assert.True(interfaceType!.IsInterface);
    }

    [Fact]
    public void ProxyClass_IsGenerated()
    {
        // Verify that the TestProxyActorProxy class type exists
        var proxyType = Type.GetType("Quark.Tests.Generated.TestProxyActorProxy, Quark.Tests");
        Assert.NotNull(proxyType);
        Assert.False(proxyType!.IsInterface);
    }

    [Fact]
    public void ProxyClass_ImplementsInterface()
    {
        // Verify that TestProxyActorProxy implements ITestProxyActorProxy
        var interfaceType = Type.GetType("Quark.Tests.Generated.ITestProxyActorProxy, Quark.Tests");
        var proxyType = Type.GetType("Quark.Tests.Generated.TestProxyActorProxy, Quark.Tests");

        Assert.NotNull(interfaceType);
        Assert.NotNull(proxyType);
        Assert.True(interfaceType!.IsAssignableFrom(proxyType));
    }

    [Fact]
    public void ProxyClass_HasCorrectConstructor()
    {
        // Verify that TestProxyActorProxy has a constructor taking (IClusterClient, string)
        var proxyType = Type.GetType("Quark.Tests.Generated.TestProxyActorProxy, Quark.Tests");
        Assert.NotNull(proxyType);

        var constructor = proxyType!.GetConstructor(new[] { typeof(IClusterClient), typeof(string) });
        Assert.NotNull(constructor);
    }

    [Fact]
    public void ProxyInterface_HasCorrectMethods()
    {
        // Verify that ITestProxyActorProxy has the expected methods
        var interfaceType = Type.GetType("Quark.Tests.Generated.ITestProxyActorProxy, Quark.Tests");
        Assert.NotNull(interfaceType);

        var addMethod = interfaceType!.GetMethod("AddAsync");
        Assert.NotNull(addMethod);
        Assert.Equal(typeof(Task<int>), addMethod!.ReturnType);

        var getMessageMethod = interfaceType.GetMethod("GetMessageAsync");
        Assert.NotNull(getMessageMethod);
        Assert.Equal(typeof(Task<string>), getMessageMethod!.ReturnType);

        var voidMethod = interfaceType.GetMethod("VoidMethodAsync");
        Assert.NotNull(voidMethod);
        Assert.Equal(typeof(Task), voidMethod!.ReturnType);
    }
}
