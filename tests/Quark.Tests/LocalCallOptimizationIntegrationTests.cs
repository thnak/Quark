using Microsoft.Extensions.Logging;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Client;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

/// <summary>
/// Integration test demonstrating the local call optimization flow.
/// This test verifies that when a target silo matches the local silo,
/// the transport layer is invoked with the correct silo ID and can optimize accordingly.
/// </summary>
public class LocalCallOptimizationIntegrationTests
{
    [Fact]
    public async Task WhenTargetIsLocalSilo_TransportReceivesLocalSiloId()
    {
        // Arrange: Setup a scenario where client and silo are co-located
        const string localSiloId = "local-silo-123";
        const string actorId = "test-actor-456";
        const string actorType = "TestActor";

        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        // Configure transport to report it's on local silo
        mockTransport.Setup(t => t.LocalSiloId).Returns(localSiloId);
        mockTransport.Setup(t => t.LocalEndpoint).Returns("localhost:5000");
        
        // Configure cluster membership to return active silos including local
        var activeSilos = new List<SiloInfo>
        {
            new SiloInfo(localSiloId, "localhost", 5000, SiloStatus.Active),
            new SiloInfo("remote-silo-456", "remote-host", 5001, SiloStatus.Active)
        };
        mockClusterMembership
            .Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSilos);
        
        // Configure consistent hashing to place this actor on LOCAL silo
        mockClusterMembership
            .Setup(m => m.GetActorSilo(actorId, actorType))
            .Returns(localSiloId);  // Key point: actor is on local silo
        
        // Setup transport to return a mock response
        var responseEnvelope = new QuarkEnvelope(
            messageId: "msg-response-1",
            actorId: actorId,
            actorType: actorType,
            methodName: "TestMethod",
            payload: new byte[] { 1, 2, 3 })
        {
            ResponsePayload = new byte[] { 4, 5, 6 }
        };
        
        mockTransport
            .Setup(t => t.SendAsync(
                It.IsAny<string>(),
                It.IsAny<QuarkEnvelope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseEnvelope);

        var options = new ClusterClientOptions
        {
            ClientId = "test-client"
        };
        
        var mockLogger = new Mock<ILogger<ClusterClient>>();
        
        using var client = new ClusterClient(
            mockClusterMembership.Object,
            mockTransport.Object,
            options,
            mockLogger.Object);

        // Connect the client
        await client.ConnectAsync();

        // Create envelope for the actor call
        var requestEnvelope = new QuarkEnvelope(
            messageId: "msg-request-1",
            actorId: actorId,
            actorType: actorType,
            methodName: "TestMethod",
            payload: new byte[] { 1, 2, 3 });

        // Act: Send the envelope
        var response = await client.SendAsync(requestEnvelope);

        // Assert: Verify the behavior
        Assert.NotNull(response);
        Assert.Equal("local-silo-123", client.LocalSiloId);
        
        // Verify that SendAsync was called with the LOCAL silo ID
        // This is where the transport can optimize - it sees targetSiloId == LocalSiloId
        mockTransport.Verify(
            t => t.SendAsync(localSiloId, It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Transport should be called with local silo ID, allowing it to optimize the call");
        
        // Verify that the log message was called for local optimization
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Local call detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "Should log when local call optimization is detected");
    }

    [Fact]
    public async Task WhenTargetIsRemoteSilo_TransportReceivesRemoteSiloId()
    {
        // Arrange: Setup a scenario where target actor is on a different silo
        const string localSiloId = "local-silo-123";
        const string remoteSiloId = "remote-silo-456";
        const string actorId = "test-actor-789";
        const string actorType = "TestActor";

        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        
        mockTransport.Setup(t => t.LocalSiloId).Returns(localSiloId);
        mockTransport.Setup(t => t.LocalEndpoint).Returns("localhost:5000");
        
        var activeSilos = new List<SiloInfo>
        {
            new SiloInfo(localSiloId, "localhost", 5000, SiloStatus.Active),
            new SiloInfo(remoteSiloId, "remote-host", 5001, SiloStatus.Active)
        };
        mockClusterMembership
            .Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSilos);
        
        // Configure consistent hashing to place this actor on REMOTE silo
        mockClusterMembership
            .Setup(m => m.GetActorSilo(actorId, actorType))
            .Returns(remoteSiloId);  // Key point: actor is on REMOTE silo
        
        var responseEnvelope = new QuarkEnvelope(
            messageId: "msg-response-2",
            actorId: actorId,
            actorType: actorType,
            methodName: "TestMethod",
            payload: new byte[] { 1, 2, 3 })
        {
            ResponsePayload = new byte[] { 7, 8, 9 }
        };
        
        mockTransport
            .Setup(t => t.SendAsync(
                It.IsAny<string>(),
                It.IsAny<QuarkEnvelope>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseEnvelope);

        var options = new ClusterClientOptions { ClientId = "test-client" };
        var mockLogger = new Mock<ILogger<ClusterClient>>();
        
        using var client = new ClusterClient(
            mockClusterMembership.Object,
            mockTransport.Object,
            options,
            mockLogger.Object);

        await client.ConnectAsync();

        var requestEnvelope = new QuarkEnvelope(
            messageId: "msg-request-2",
            actorId: actorId,
            actorType: actorType,
            methodName: "TestMethod",
            payload: new byte[] { 1, 2, 3 });

        // Act
        var response = await client.SendAsync(requestEnvelope);

        // Assert
        Assert.NotNull(response);
        
        // Verify that SendAsync was called with the REMOTE silo ID
        mockTransport.Verify(
            t => t.SendAsync(remoteSiloId, It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Transport should be called with remote silo ID for network call");
        
        // Verify that local optimization log was NOT called
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Local call detected")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            "Should NOT log local optimization for remote calls");
    }
}
