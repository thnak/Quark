using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Quark.Abstractions.Clustering;
using Quark.Placement.Locality;
using Xunit;

namespace Quark.Tests;

public class LocalityAwarePlacementTests
{
    [Fact]
    public void CommunicationPatternAnalyzer_RecordInteraction_TracksMessage()
    {
        // Arrange
        var analyzer = new CommunicationPatternAnalyzer();

        // Act
        analyzer.RecordInteraction("actor-1", "actor-2", 100);

        // Assert
        var graph = analyzer.GetCommunicationGraphAsync(TimeSpan.FromMinutes(5)).Result;
        var metrics = graph.GetMetrics("actor-1", "actor-2");
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.MessageCount);
        Assert.Equal(100, metrics.TotalBytes);
    }

    [Fact]
    public void CommunicationPatternAnalyzer_GetHotPairs_ReturnsTopPairs()
    {
        // Arrange
        var analyzer = new CommunicationPatternAnalyzer();
        analyzer.RecordInteraction("actor-1", "actor-2", 100);
        analyzer.RecordInteraction("actor-1", "actor-2", 100);
        analyzer.RecordInteraction("actor-1", "actor-2", 100);
        analyzer.RecordInteraction("actor-3", "actor-4", 100);

        // Act
        var hotPairs = analyzer.GetHotPairsAsync(10).Result;

        // Assert
        Assert.Equal(2, hotPairs.Count);
        Assert.Equal("actor-1", hotPairs[0].FromActorId);
        Assert.Equal("actor-2", hotPairs[0].ToActorId);
        Assert.Equal(3, hotPairs[0].Metrics.MessageCount);
    }

    [Fact]
    public void CommunicationPatternAnalyzer_ClearOldData_RemovesOldEntries()
    {
        // Arrange
        var analyzer = new CommunicationPatternAnalyzer();
        analyzer.RecordInteraction("actor-1", "actor-2", 100);

        // Act
        analyzer.ClearOldData(TimeSpan.Zero); // Clear everything
        var graph = analyzer.GetCommunicationGraphAsync(TimeSpan.FromMinutes(5)).Result;

        // Assert
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void LocalityAwarePlacementPolicy_SelectSilo_ReturnsNullWhenNoSilosAvailable()
    {
        // Arrange
        var analyzerMock = new Mock<ICommunicationPatternAnalyzer>();
        var directoryMock = new Mock<IActorDirectory>();
        var loggerMock = new Mock<ILogger<LocalityAwarePlacementPolicy>>();
        var options = Options.Create(new LocalityAwarePlacementOptions());
        
        var policy = new LocalityAwarePlacementPolicy(
            analyzerMock.Object,
            directoryMock.Object,
            options,
            loggerMock.Object);

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", Array.Empty<string>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void LocalityAwarePlacementPolicy_SelectSilo_ReturnsSingleSilo()
    {
        // Arrange
        var analyzerMock = new Mock<ICommunicationPatternAnalyzer>();
        var directoryMock = new Mock<IActorDirectory>();
        var loggerMock = new Mock<ILogger<LocalityAwarePlacementPolicy>>();
        var options = Options.Create(new LocalityAwarePlacementOptions());
        
        var policy = new LocalityAwarePlacementPolicy(
            analyzerMock.Object,
            directoryMock.Object,
            options,
            loggerMock.Object);

        var silos = new[] { "silo-1" };

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", silos);

        // Assert
        Assert.Equal("silo-1", result);
    }

    [Fact]
    public void LocalityAwarePlacementPolicy_SelectSilo_FallsBackToRandomWhenNoLocalityData()
    {
        // Arrange
        var analyzerMock = new Mock<ICommunicationPatternAnalyzer>();
        analyzerMock
            .Setup(a => a.GetCommunicationGraphAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(new CommunicationGraph());

        var directoryMock = new Mock<IActorDirectory>();
        var loggerMock = new Mock<ILogger<LocalityAwarePlacementPolicy>>();
        var options = Options.Create(new LocalityAwarePlacementOptions());
        
        var policy = new LocalityAwarePlacementPolicy(
            analyzerMock.Object,
            directoryMock.Object,
            options,
            loggerMock.Object);

        var silos = new[] { "silo-1", "silo-2", "silo-3" };

        // Act
        var result = policy.SelectSilo("actor-1", "TestActor", silos);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, silos);
    }
}
