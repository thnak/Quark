# Actor Queries Example

This example demonstrates the actor query capabilities in Quark, allowing you to query and analyze active actors using LINQ-style syntax.

## Features Demonstrated

1. **Query by Type**: Filter actors by their type
2. **Query by ID Pattern**: Use wildcards to match actor IDs
3. **Aggregate Statistics**: Count actors, group by type, get top N
4. **Pagination**: Query large actor populations efficiently
5. **HTTP Endpoints**: Access query functionality via REST API

## Running the Example

```bash
dotnet run --project examples/Quark.Examples.ActorQueries
```

The application will:
1. Start a Quark silo with in-memory clustering
2. Create various types of actors (Users, Orders, Products)
3. Demonstrate different query patterns
4. Expose HTTP endpoints for querying actors

## HTTP Endpoints

Once running, you can access these endpoints:

### Query Actors
```bash
# Get all actors with pagination
curl "http://localhost:5000/quark/actors/query?page=1&pageSize=10"

# Filter by type
curl "http://localhost:5000/quark/actors/query?type=UserActor"

# Filter by ID pattern (wildcards supported)
curl "http://localhost:5000/quark/actors/query?idPattern=user-*"

# Combine filters
curl "http://localhost:5000/quark/actors/query?type=OrderActor&idPattern=order-2024*&page=1&pageSize=20"
```

### Get Statistics
```bash
# Get aggregate statistics
curl "http://localhost:5000/quark/actors/stats"

# Get actor count
curl "http://localhost:5000/quark/actors/count"

# Get actor count by type
curl "http://localhost:5000/quark/actors/count?type=UserActor"

# List all actor types
curl "http://localhost:5000/quark/actors/types"
```

## Query API Usage

The example also demonstrates programmatic usage:

```csharp
// Inject IActorQueryService
var queryService = serviceProvider.GetRequiredService<IActorQueryService>();

// Query by predicate
var users = await queryService.QueryActorsAsync(
    actor => actor.ActorId.StartsWith("user-"));

// Query by type
var orders = await queryService.QueryActorsByTypeAsync<OrderActor>();

// Query with pagination
var result = await queryService.QueryActorMetadataAsync(
    metadata => metadata.ActorType == "UserActor",
    pageNumber: 1,
    pageSize: 10);

// Get statistics
var stats = await queryService.GroupActorsByTypeAsync();
var topActors = await queryService.GetTopActorsAsync(m => m.ActorId, count: 10);
```

## Architecture

- **Quark.Queries**: Core query infrastructure
- **IActorQueryService**: Main query interface
- **ActorMetadata**: Queryable actor properties
- **ActorQueryResult<T>**: Paginated query results
- **HTTP Endpoints**: REST API for remote querying

## Use Cases

This functionality is useful for:
- Cluster monitoring dashboards
- Actor population analytics
- Capacity planning
- Anomaly detection
- Load testing and performance analysis
