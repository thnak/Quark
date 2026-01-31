using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Quark.Abstractions;
using Quark.Hosting;
using Quark.Queries;

namespace Quark.Tests;

public class ActorQueryServiceTests
{
    [Fact]
    public async Task GetAllActorsAsync_ReturnsAllActiveActors()
    {
        // Arrange
        var actor1 = new TestActor("actor-1");
        var actor2 = new TestActor("actor-2");
        var actor3 = new TestActor("actor-3");
        
        var silo = CreateMockSilo(actor1, actor2, actor3);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var result = await queryService.GetAllActorsAsync();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(actor1, result);
        Assert.Contains(actor2, result);
        Assert.Contains(actor3, result);
    }

    [Fact]
    public async Task QueryActorsAsync_FiltersByPredicate()
    {
        // Arrange
        var actor1 = new TestActor("user-1");
        var actor2 = new TestActor("order-1");
        var actor3 = new TestActor("user-2");
        
        var silo = CreateMockSilo(actor1, actor2, actor3);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var result = await queryService.QueryActorsAsync(a => a.ActorId.StartsWith("user-"));

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(actor1, result);
        Assert.Contains(actor3, result);
        Assert.DoesNotContain(actor2, result);
    }

    [Fact]
    public async Task QueryActorsByTypeAsync_FiltersCorrectly()
    {
        // Arrange
        var actor1 = new TestActor("actor-1");
        var actor2 = new OtherTestActor("actor-2");
        var actor3 = new TestActor("actor-3");
        
        var silo = CreateMockSilo(actor1, actor2, actor3);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var result = await queryService.QueryActorsByTypeAsync<TestActor>();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(actor1, result);
        Assert.Contains(actor3, result);
    }

    [Fact]
    public async Task QueryActorsByIdPatternAsync_SupportsWildcards()
    {
        // Arrange
        var actor1 = new TestActor("user-123");
        var actor2 = new TestActor("user-456");
        var actor3 = new TestActor("order-789");
        
        var silo = CreateMockSilo(actor1, actor2, actor3);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var result = await queryService.QueryActorsByIdPatternAsync("user-*");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(actor1, result);
        Assert.Contains(actor2, result);
        Assert.DoesNotContain(actor3, result);
    }

    [Fact]
    public async Task GetActorMetadataAsync_ReturnsMetadataForAllActors()
    {
        // Arrange
        var actor1 = new TestActor("actor-1");
        var actor2 = new TestActor("actor-2");
        
        var silo = CreateMockSilo(actor1, actor2);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var result = await queryService.GetActorMetadataAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, metadata =>
        {
            Assert.NotNull(metadata.ActorId);
            Assert.NotNull(metadata.ActorType);
            Assert.NotNull(metadata.FullTypeName);
        });
    }

    [Fact]
    public async Task QueryActorMetadataAsync_SupportsPagination()
    {
        // Arrange
        var actors = Enumerable.Range(1, 25)
            .Select(i => new TestActor($"actor-{i}"))
            .ToArray<IActor>();
        
        var silo = CreateMockSilo(actors);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act - Get first page
        var page1 = await queryService.QueryActorMetadataAsync(_ => true, pageNumber: 1, pageSize: 10);

        // Assert - First page
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(1, page1.PageNumber);
        Assert.Equal(10, page1.PageSize);
        Assert.Equal(3, page1.TotalPages);
        Assert.True(page1.HasNextPage);
        Assert.False(page1.HasPreviousPage);

        // Act - Get second page
        var page2 = await queryService.QueryActorMetadataAsync(_ => true, pageNumber: 2, pageSize: 10);

        // Assert - Second page
        Assert.Equal(10, page2.Items.Count);
        Assert.True(page2.HasNextPage);
        Assert.True(page2.HasPreviousPage);

        // Act - Get last page
        var page3 = await queryService.QueryActorMetadataAsync(_ => true, pageNumber: 3, pageSize: 10);

        // Assert - Last page
        Assert.Equal(5, page3.Items.Count);
        Assert.False(page3.HasNextPage);
        Assert.True(page3.HasPreviousPage);
    }

    [Fact]
    public async Task CountActorsAsync_ReturnsCorrectCount()
    {
        // Arrange
        var actors = Enumerable.Range(1, 42)
            .Select(i => new TestActor($"actor-{i}"))
            .ToArray<IActor>();
        
        var silo = CreateMockSilo(actors);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var count = await queryService.CountActorsAsync();

        // Assert
        Assert.Equal(42, count);
    }

    [Fact]
    public async Task CountActorsAsync_WithPredicate_FiltersCorrectly()
    {
        // Arrange
        var actors = Enumerable.Range(1, 10)
            .Select(i => new TestActor($"actor-{i}"))
            .ToArray<IActor>();
        
        var silo = CreateMockSilo(actors);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var count = await queryService.CountActorsAsync(m => m.ActorType == "TestActor");

        // Assert
        Assert.Equal(10, count);
    }

    [Fact]
    public async Task GroupActorsByTypeAsync_GroupsCorrectly()
    {
        // Arrange
        var actor1 = new TestActor("actor-1");
        var actor2 = new TestActor("actor-2");
        var actor3 = new OtherTestActor("actor-3");
        var actor4 = new OtherTestActor("actor-4");
        var actor5 = new OtherTestActor("actor-5");
        
        var silo = CreateMockSilo(actor1, actor2, actor3, actor4, actor5);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var grouped = await queryService.GroupActorsByTypeAsync();

        // Assert
        Assert.Equal(2, grouped.Count);
        Assert.Equal(2, grouped["TestActor"]);
        Assert.Equal(3, grouped["OtherTestActor"]);
    }

    [Fact]
    public async Task GetTopActorsAsync_ReturnsCorrectCount()
    {
        // Arrange
        var actors = Enumerable.Range(1, 20)
            .Select(i => new TestActor($"actor-{i}"))
            .ToArray<IActor>();
        
        var silo = CreateMockSilo(actors);
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act
        var top5 = await queryService.GetTopActorsAsync(m => m.ActorId, count: 5);

        // Assert
        Assert.Equal(5, top5.Count);
    }

    [Fact]
    public async Task QueryActorsAsync_ThrowsWhenPredicateIsNull()
    {
        // Arrange
        var silo = CreateMockSilo();
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            queryService.QueryActorsAsync(null!));
    }

    [Fact]
    public async Task QueryActorsByIdPatternAsync_ThrowsWhenPatternIsEmpty()
    {
        // Arrange
        var silo = CreateMockSilo();
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            queryService.QueryActorsByIdPatternAsync(string.Empty));
    }

    [Fact]
    public async Task QueryActorMetadataAsync_ThrowsWhenPageNumberIsInvalid()
    {
        // Arrange
        var silo = CreateMockSilo();
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            queryService.QueryActorMetadataAsync(_ => true, pageNumber: 0, pageSize: 10));
    }

    [Fact]
    public async Task QueryActorMetadataAsync_ThrowsWhenPageSizeIsTooLarge()
    {
        // Arrange
        var silo = CreateMockSilo();
        var queryService = new ActorQueryService(silo, NullLogger<ActorQueryService>.Instance);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            queryService.QueryActorMetadataAsync(_ => true, pageNumber: 1, pageSize: 1001));
    }

    private static IQuarkSilo CreateMockSilo(params IActor[] actors)
    {
        var mock = new Mock<IQuarkSilo>();
        mock.Setup(s => s.GetActiveActors())
            .Returns((IReadOnlyCollection<IActor>)actors.ToList());
        return mock.Object;
    }

    [Actor(Name = "TestActor")]
    private class TestActor : IActor
    {
        public TestActor(string actorId)
        {
            ActorId = actorId;
        }

        public string ActorId { get; }

        public Task OnActivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Actor(Name = "OtherTestActor")]
    private class OtherTestActor : IActor
    {
        public OtherTestActor(string actorId)
        {
            ActorId = actorId;
        }

        public string ActorId { get; }

        public Task OnActivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task OnDeactivateAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
