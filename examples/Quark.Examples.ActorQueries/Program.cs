using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Hosting;
using Quark.Queries;
using Quark.Examples.ActorQueries;

Console.WriteLine("=== Actor Queries Example ===");
Console.WriteLine();

// Create an actor factory
var factory = new ActorFactory();
Console.WriteLine("Creating sample actors...");

// Create 10 users
for (int i = 1; i <= 10; i++)
{
    var user = factory.GetOrCreateActor<UserActor>($"user-{i:D3}");
    await user.OnActivateAsync();
    await user.SetEmailAsync($"user{i}@example.com");
}

// Create 15 orders
for (int i = 1; i <= 15; i++)
{
    var order = factory.GetOrCreateActor<OrderActor>($"order-2024-{i:D4}");
    await order.OnActivateAsync();
    await order.SetTotalAsync(99.99m * i);
    await order.UpdateStatusAsync(i % 3 == 0 ? "completed" : "pending");
}

// Create 5 products
for (int i = 1; i <= 5; i++)
{
    var product = factory.GetOrCreateActor<ProductActor>($"product-{i}");
    await product.OnActivateAsync();
    await product.SetDetailsAsync($"Product {i}", 49.99m * i);
}

Console.WriteLine($"Created 30 actors (10 users, 15 orders, 5 products)");
Console.WriteLine();

// Create a mock silo to use with the query service
var mockSilo = new MockQuarkSilo(factory);
var queryService = new ActorQueryService(mockSilo, NullLogger<ActorQueryService>.Instance);

Console.WriteLine("=== Demonstrating Query Capabilities ===");
Console.WriteLine();

// 1. Query all actors
var allActors = await queryService.GetAllActorsAsync();
Console.WriteLine($"1. Total active actors: {allActors.Count}");

// 2. Query by type
var users = await queryService.QueryActorsByTypeAsync<UserActor>();
Console.WriteLine($"2. User actors: {users.Count}");

var orders = await queryService.QueryActorsByTypeAsync<OrderActor>();
Console.WriteLine($"   Order actors: {orders.Count}");

var products = await queryService.QueryActorsByTypeAsync<ProductActor>();
Console.WriteLine($"   Product actors: {products.Count}");

// 3. Query by ID pattern
var user0xx = await queryService.QueryActorsByIdPatternAsync("user-0*");
Console.WriteLine($"3. Users matching 'user-0*': {user0xx.Count} ({string.Join(", ", user0xx.Take(3).Select(a => a.ActorId))})");

var orders2024 = await queryService.QueryActorsByIdPatternAsync("order-2024-*");
Console.WriteLine($"   Orders matching 'order-2024-*': {orders2024.Count}");

// 4. Get statistics
var stats = await queryService.GroupActorsByTypeAsync();
Console.WriteLine("4. Actors by type:");
foreach (var (type, count) in stats)
{
    Console.WriteLine($"   - {type}: {count}");
}

// 5. Query with pagination
Console.WriteLine("5. Querying with pagination (page 1, size 5):");
var page1 = await queryService.QueryActorMetadataAsync(
    metadata => true,
    pageNumber: 1,
    pageSize: 5);

Console.WriteLine($"   Page {page1.PageNumber} of {page1.TotalPages} (Total: {page1.TotalCount})");
foreach (var metadata in page1.Items)
{
    Console.WriteLine($"   - {metadata.ActorType}: {metadata.ActorId}");
}

// 6. Query with custom predicate
var userActorMetadata = await queryService.QueryActorMetadataAsync(
    metadata => metadata.ActorType == "UserActor",
    pageNumber: 1,
    pageSize: 10);
Console.WriteLine($"6. Query result for UserActor: {userActorMetadata.TotalCount} found");

// 7. Get top actors
var topActors = await queryService.GetTopActorsAsync(m => m.ActorId, count: 5);
Console.WriteLine($"7. Top 5 actors by ID: {string.Join(", ", topActors.Select(m => m.ActorId))}");

Console.WriteLine();
Console.WriteLine("=== Example completed successfully ===");

// Mock silo implementation for standalone example
class MockQuarkSilo : IQuarkSilo
{
    private readonly IActorFactory _factory;

    public MockQuarkSilo(IActorFactory factory)
    {
        _factory = factory;
        SiloId = "example-silo";
        SiloInfo = new Quark.Abstractions.Clustering.SiloInfo(
            SiloId, 
            "localhost", 
            5000, 
            Quark.Abstractions.Clustering.SiloStatus.Active);
    }

    public string SiloId { get; }
    public Quark.Abstractions.Clustering.SiloStatus Status => Quark.Abstractions.Clustering.SiloStatus.Active;
    public Quark.Abstractions.Clustering.SiloInfo SiloInfo { get; }
    public IActorFactory ActorFactory => _factory;

    public IReadOnlyCollection<IActor> GetActiveActors()
    {
        // Access the private _actors field using reflection
        var actorsField = _factory.GetType().GetField("_actors", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (actorsField?.GetValue(_factory) is System.Collections.Concurrent.ConcurrentDictionary<(Type, string), IActor> actors)
        {
            return actors.Values.ToList();
        }
        
        return Array.Empty<IActor>();
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Dispose() { }
}
