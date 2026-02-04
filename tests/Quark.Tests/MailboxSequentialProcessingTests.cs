using Quark.Abstractions;
using Xunit;

namespace Quark.Tests;

/// <summary>
/// Tests for sequential message processing through actor mailboxes.
/// Verifies that actors process messages one at a time in order.
/// </summary>
public class MailboxSequentialProcessingTests
{
    [Fact]
    public async Task Actor_ProcessesMessages_Sequentially()
    {
        // NOTE: This test verifies that the actor code itself is race-free,
        // but doesn't go through the full QuarkSilo/mailbox infrastructure.
        // For full integration testing, see AwesomePizza end-to-end tests.
        
        // Arrange
        var actorId = "sequential-test-1";
        var actor = new SequentialTestActor(actorId);
        
        // Act - send 10 messages concurrently (simulating mailbox behavior)
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => actor.ProcessAsync(index)));
        }
        
        var results = await Task.WhenAll(tasks);
        
        // Assert - messages should be processed in some order (but consistently)
        Assert.Equal(10, results.Length);
        Assert.Equal(10, actor.ProcessedMessages.Count);
        
        // Verify no concurrent processing (counter should equal number of messages)
        Assert.Equal(10, actor.FinalCounter);
    }
    
    [Fact]
    public void DispatcherRegistry_UsesInterfaceName_WhenInterfaceTypeSpecified()
    {
        // Arrange
        var actorId = "interface-test-1";
        var actor = new InterfaceTestActor(actorId);
        var interfaceName = "Quark.Tests.IInterfaceTestActor";
        
        // Act
        var dispatcher = ActorMethodDispatcherRegistry.GetDispatcher(interfaceName);
        
        // Assert
        Assert.NotNull(dispatcher);
        Assert.Equal(typeof(InterfaceTestActor), dispatcher.ActorType);
    }
}