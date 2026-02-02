using ProtoBuf;
using Quark.Abstractions;
using Quark.Core.Actors;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for the actor method dispatcher system.
/// Verifies that generated dispatchers can invoke actor methods correctly.
/// </summary>
public class ActorMethodDispatcherTests
{
    [Fact]
    public async Task Dispatcher_InvokesActorMethod_Successfully()
    {
        // Arrange
        var actorId = "test-dispatcher-1";
        var actor = new MailboxTestActor(actorId);
        var dispatcher = ActorMethodDispatcherRegistry.GetDispatcher("MailboxTestActor");
        
        Assert.NotNull(dispatcher);
        
        // Act - invoke TestMethod via dispatcher
        var payload = Array.Empty<byte>(); // TestMethod has no parameters
        var resultBytes = await dispatcher.InvokeAsync(actor, "TestMethod", payload, default);
        
        // Deserialize Protobuf response
        using (var ms = new System.IO.MemoryStream(resultBytes))
        {
            var response = Serializer.Deserialize<MailboxTestActor_TestMethodResponse>(ms);
            Assert.Equal("test result", response.Result);
        }
    }
    
    [Fact]
    public void Dispatcher_Registry_ContainsRegisteredActors()
    {
        // Arrange & Act
        var registeredTypes = ActorMethodDispatcherRegistry.GetRegisteredActorTypes();
        
        // Assert
        Assert.NotEmpty(registeredTypes);
        Assert.Contains("MailboxTestActor", registeredTypes);
    }
    
    [Fact]
    public async Task Dispatcher_ThrowsException_ForInvalidMethodName()
    {
        // Arrange
        var actorId = "test-dispatcher-2";
        var actor = new MailboxTestActor(actorId);
        var dispatcher = ActorMethodDispatcherRegistry.GetDispatcher("MailboxTestActor");
        
        Assert.NotNull(dispatcher);
        
        // Act & Assert
        var payload = Array.Empty<byte>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await dispatcher.InvokeAsync(actor, "NonExistentMethod", payload, default);
        });
    }
    
    [Fact]
    public async Task Dispatcher_ThrowsException_ForWrongActorType()
    {
        // Arrange
        var actorId = "test-dispatcher-3";
        var actor = new MailboxTestActor(actorId);
        var dispatcher = ActorMethodDispatcherRegistry.GetDispatcher("MailboxTestActor");
        
        // Create a different actor type
        var wrongActor = new CustomSupervisorActor("wrong-actor");
        
        Assert.NotNull(dispatcher);
        
        // Act & Assert
        var payload = Array.Empty<byte>();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await dispatcher.InvokeAsync(wrongActor, "TestMethod", payload, default);
        });
    }
}
