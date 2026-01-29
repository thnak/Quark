using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions;
using Quark.Abstractions.Clustering;
using Quark.Core.Actors;
using Quark.Hosting;
using Quark.Networking.Abstractions;

namespace Quark.Tests;

public class QuarkSiloTests
{
    [Fact]
    public void QuarkSilo_Constructor_SetsSiloId()
    {
        // Arrange
        var actorFactory = new ActorFactory();
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new QuarkSiloOptions { SiloId = "test-silo-1" };
        var logger = NullLogger<QuarkSilo>.Instance;

        // Act
        using var silo = new QuarkSilo(actorFactory, mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Assert
        Assert.Equal("test-silo-1", silo.SiloId);
        Assert.Equal(SiloStatus.Joining, silo.Status);
        Assert.NotNull(silo.ActorFactory);
    }

    [Fact]
    public void QuarkSilo_Constructor_GeneratesSiloIdWhenNotProvided()
    {
        // Arrange
        var actorFactory = new ActorFactory();
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new QuarkSiloOptions();
        var logger = NullLogger<QuarkSilo>.Instance;

        // Act
        using var silo = new QuarkSilo(actorFactory, mockClusterMembership.Object, mockTransport.Object, options, logger);

        // Assert
        Assert.NotNull(silo.SiloId);
        Assert.NotEmpty(silo.SiloId);
    }

    [Fact]
    public void QuarkSilo_GetActiveActors_ReturnsEmptyInitially()
    {
        // Arrange
        var actorFactory = new ActorFactory();
        var mockClusterMembership = new Mock<IQuarkClusterMembership>();
        var mockTransport = new Mock<IQuarkTransport>();
        var options = new QuarkSiloOptions { SiloId = "test-silo-2" };
        var logger = NullLogger<QuarkSilo>.Instance;

        // Act
        using var silo = new QuarkSilo(actorFactory, mockClusterMembership.Object, mockTransport.Object, options, logger);
        var activeActors = silo.GetActiveActors();

        // Assert
        Assert.NotNull(activeActors);
        Assert.Empty(activeActors);
    }

    [Fact]
    public void QuarkSiloOptions_DefaultValues_AreSet()
    {
        // Act
        var options = new QuarkSiloOptions();

        // Assert
        Assert.Equal("localhost", options.Address);
        Assert.Equal(11111, options.Port);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HeartbeatInterval);
        Assert.True(options.EnableReminders);
        Assert.True(options.EnableStreaming);
    }
}
