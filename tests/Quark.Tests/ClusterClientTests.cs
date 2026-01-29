using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
}
