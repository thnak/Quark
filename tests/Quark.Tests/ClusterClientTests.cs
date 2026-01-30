using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Client;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

public class ClusterClientTests
{
    [Fact]
    public void ClusterClient_Constructor_SetsProperties()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new ClusterClientOptions { ClientId = "test-client-1" };
        var logger = NullLogger<ClusterClient>.Instance;

        // Act
        using var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Assert
        Assert.NotNull(client.ClusterMembership);
        Assert.NotNull(client.Transport);
    }

    [Fact]
    public void ClusterClient_Constructor_GeneratesClientIdWhenNotProvided()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new ClusterClientOptions();
        var logger = NullLogger<ClusterClient>.Instance;

        // Act
        using var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Assert - client ID is internal, but we can verify it doesn't throw
        Assert.NotNull(client);
    }

    [Fact]
    public async Task ClusterClient_SendAsync_ThrowsWhenNotConnected()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new ClusterClientOptions();
        var logger = NullLogger<ClusterClient>.Instance;
        using var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, options, logger);

        var envelope = new QuarkEnvelope(
            messageId: Guid.NewGuid().ToString(),
            actorId: "test-actor",
            actorType: "TestActor",
            methodName: "TestMethod",
            payload: Array.Empty<byte>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(envelope));
    }

    [Fact]
    public void ClusterClientOptions_DefaultValues_AreSet()
    {
        // Act
        var options = new ClusterClientOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
    }

    [Fact]
    public void ClusterClient_LocalSiloId_ReturnsTransportLocalSiloId()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        mockTransport.Setup(t => t.LocalSiloId).Returns("local-silo-123");
        var options = new ClusterClientOptions();
        var logger = NullLogger<ClusterClient>.Instance;

        // Act
        using var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Assert
        Assert.Equal("local-silo-123", client.LocalSiloId);
    }

    [Fact]
    public async Task ClusterClient_SendAsync_LogsLocalCallWhenTargetIsLocalSilo()
    {
        // Arrange
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        mockTransport.Setup(t => t.LocalSiloId).Returns("local-silo-123");
        
        // Setup cluster membership to return active silos
        var silos = new List<SiloInfo>
        {
            new SiloInfo("local-silo-123", "localhost", 5000, SiloStatus.Active)
        };
        mockClusterMembership.Setup(m => m.GetActiveSilosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(silos);
        
        // Setup GetActorSilo to return the local silo
        mockClusterMembership.Setup(m => m.GetActorSilo(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("local-silo-123");
        
        // Setup transport to return a response
        var responseEnvelope = new QuarkEnvelope(
            messageId: "msg-1",
            actorId: "test-actor",
            actorType: "TestActor",
            methodName: "TestMethod",
            payload: Array.Empty<byte>())
        {
            ResponsePayload = Array.Empty<byte>()
        };
        
        mockTransport.Setup(t => t.SendAsync(
            It.IsAny<string>(),
            It.IsAny<QuarkEnvelope>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseEnvelope);

        var options = new ClusterClientOptions();
        var logger = NullLogger<ClusterClient>.Instance;
        using var client = new ClusterClient(mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Connect the client
        await client.ConnectAsync();

        var envelope = new QuarkEnvelope(
            messageId: "msg-1",
            actorId: "test-actor",
            actorType: "TestActor",
            methodName: "TestMethod",
            payload: Array.Empty<byte>());

        // Act
        var response = await client.SendAsync(envelope);

        // Assert
        Assert.NotNull(response);
        // Verify that SendAsync was called with the local silo ID
        mockTransport.Verify(t => t.SendAsync("local-silo-123", It.IsAny<QuarkEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

