# Community Features - Quick Implementation Guide

**Target Audience:** Developers implementing features from section 10.7  
**Companion Document:** `COMMUNITY_FEATURES_ROADMAP.md` (detailed planning)  
**Last Updated:** 2026-01-31

This guide provides practical implementation patterns and guidelines for building the community-requested features while maintaining Quark's core principles.

---

## Core Implementation Principles

### 1. AOT-First Development

Every feature MUST be Native AOT compatible:

```csharp
// ✅ GOOD: Source-generated serialization
[JsonSerializable(typeof(EventData))]
public partial class EventSerializationContext : JsonSerializerContext { }

// ❌ BAD: Runtime reflection
var type = Type.GetType(eventTypeName);
var instance = Activator.CreateInstance(type);

// ✅ GOOD: Static factory registration
public static class EventFactory
{
    private static readonly Dictionary<string, Func<EventData>> _factories = new();
    
    [ModuleInitializer]
    public static void RegisterFactories()
    {
        Register("OrderPlaced", () => new OrderPlacedEvent());
        Register("OrderShipped", () => new OrderShippedEvent());
    }
}
```

### 2. Zero-Allocation Hot Paths

Minimize allocations in frequently-executed code:

```csharp
// ✅ GOOD: Object pooling
private static readonly ObjectPool<StringBuilder> _builderPool = 
    ObjectPool.Create<StringBuilder>();

public string BuildMessage()
{
    var builder = _builderPool.Get();
    try
    {
        builder.Append("Message: ");
        builder.Append(data);
        return builder.ToString();
    }
    finally
    {
        builder.Clear();
        _builderPool.Return(builder);
    }
}

// ✅ GOOD: Span<T> for buffers
public void ProcessBuffer(ReadOnlySpan<byte> data)
{
    // No heap allocation
}

// ✅ GOOD: ValueTask for sync paths
public ValueTask<int> GetCountAsync()
{
    if (_cached)
        return new ValueTask<int>(_cachedValue);
    return new ValueTask<int>(LoadFromDatabaseAsync());
}
```

### 3. Incremental Source Generators

Use incremental generators for compile-time code generation:

```csharp
[Generator(LanguageNames.CSharp)]
public class EventSourcingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register syntax provider with efficient filtering
        var eventsToGenerate = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => node is ClassDeclarationSyntax cls && 
                                       cls.AttributeLists.Count > 0,
                transform: (ctx, _) => GetSemanticTargetOrNull(ctx))
            .Where(m => m is not null);
        
        // Generate code only when inputs change
        context.RegisterSourceOutput(eventsToGenerate, GenerateEventFactory);
    }
}
```

---

## Feature Implementation Patterns

## 1. Journaling/Event Sourcing

### Basic Event Store Implementation

```csharp
public class PostgresEventStore : IEventStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public async Task<long> AppendEventsAsync(
        string actorId,
        IReadOnlyList<DomainEvent> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        
        try
        {
            // Optimistic concurrency check
            if (expectedVersion.HasValue)
            {
                var currentVersion = await GetCurrentVersionAsync(
                    actorId, connection, cancellationToken);
                
                if (currentVersion != expectedVersion.Value)
                {
                    throw new EventStoreConcurrencyException(
                        actorId, expectedVersion.Value, currentVersion);
                }
            }
            
            // Append events
            long newVersion = expectedVersion ?? 0;
            foreach (var @event in events)
            {
                newVersion++;
                await InsertEventAsync(
                    connection, actorId, @event, newVersion, cancellationToken);
            }
            
            await transaction.CommitAsync(cancellationToken);
            return newVersion;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    
    private async Task InsertEventAsync(
        NpgsqlConnection connection,
        string actorId,
        DomainEvent @event,
        long sequenceNumber,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO events (actor_id, sequence_number, event_type, payload, timestamp)
            VALUES ($1, $2, $3, $4, $5)";
        
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue(actorId);
        cmd.Parameters.AddWithValue(sequenceNumber);
        cmd.Parameters.AddWithValue(@event.GetType().Name);
        cmd.Parameters.AddWithValue(JsonSerializer.Serialize(@event, _jsonOptions));
        cmd.Parameters.AddWithValue(@event.Timestamp);
        
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

### Event Replay with Snapshot Optimization

```csharp
public abstract class EventSourcedActor : ActorBase
{
    private const int SnapshotInterval = 100; // Snapshot every 100 events
    
    protected async Task RecoverStateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _eventStore.LoadSnapshotAsync(ActorId, cancellationToken);
        var fromVersion = 0L;
        
        if (snapshot.HasValue)
        {
            ApplySnapshot(snapshot.Value.Snapshot);
            fromVersion = snapshot.Value.Version + 1;
            _version = snapshot.Value.Version;
        }
        
        var events = await _eventStore.ReadEventsAsync(ActorId, fromVersion, cancellationToken);
        var eventCount = 0;
        
        foreach (var @event in events)
        {
            Apply(@event);
            _version = @event.SequenceNumber;
            eventCount++;
        }
        
        // Create snapshot if many events replayed
        if (eventCount >= SnapshotInterval)
        {
            await SaveSnapshotAsync(cancellationToken);
        }
    }
}
```

---

## 2. Durable Jobs

### Job Queue Implementation Pattern

```csharp
public class RedisJobQueue : IJobQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _queueKey = "jobs:pending";
    private readonly string _processingKey = "jobs:processing";
    
    public async Task<string> EnqueueAsync(
        Job job, 
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        
        job.JobId = GenerateJobId();
        job.Status = JobStatus.Pending;
        job.CreatedAt = DateTimeOffset.UtcNow;
        
        // Store job data
        await db.StringSetAsync(
            $"job:{job.JobId}",
            JsonSerializer.Serialize(job));
        
        // Add to priority queue
        var score = CalculateScore(job.Priority, job.ScheduledAt);
        await db.SortedSetAddAsync(_queueKey, job.JobId, score);
        
        return job.JobId;
    }
    
    public async Task<Job?> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // Atomically move job from pending to processing using Lua script
        var script = @"
            local jobId = redis.call('ZRANGE', KEYS[1], 0, 0, 'BYSCORE', '-inf', ARGV[1])
            if #jobId > 0 then
                redis.call('ZREM', KEYS[1], jobId[1])
                redis.call('ZADD', KEYS[2], ARGV[1], jobId[1])
                return jobId[1]
            end
            return nil";
        
        var jobId = (string?)await db.ScriptEvaluateAsync(
            script,
            new RedisKey[] { _queueKey, _processingKey },
            new RedisValue[] { now });
        
        if (jobId == null)
            return null;
        
        var jobData = await db.StringGetAsync($"job:{jobId}");
        return JsonSerializer.Deserialize<Job>(jobData!);
    }
    
    private static double CalculateScore(int priority, DateTimeOffset? scheduledAt)
    {
        // Higher priority = lower score (processed first)
        // Scheduled time also affects score
        var baseScore = scheduledAt?.ToUnixTimeSeconds() ?? 
                       DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return baseScore - (priority * 1000);
    }
}
```

### Job Worker Actor Pattern

```csharp
[Actor(Name = "JobWorker", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 20)]
public class JobWorkerActor : StatelessActorBase
{
    private readonly IJobQueue _jobQueue;
    private readonly IServiceProvider _serviceProvider;
    
    public async Task ProcessJobsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await _jobQueue.DequeueAsync(cancellationToken);
            if (job == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }
            
            await ExecuteJobWithRetryAsync(job, cancellationToken);
        }
    }
    
    private async Task ExecuteJobWithRetryAsync(Job job, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var retryPolicy = job.RetryPolicy ?? RetryPolicy.Default;
        
        while (retryCount <= retryPolicy.MaxRetries)
        {
            try
            {
                // Resolve job handler
                var handler = ResolveJobHandler(job.JobType);
                
                // Execute with timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (job.Timeout.HasValue)
                    cts.CancelAfter(job.Timeout.Value);
                
                var result = await handler.ExecuteAsync(job.Payload, cts.Token);
                
                // Mark as complete
                await _jobQueue.CompleteAsync(job.JobId, result, cancellationToken);
                return;
            }
            catch (Exception ex) when (retryCount < retryPolicy.MaxRetries)
            {
                retryCount++;
                var delay = CalculateRetryDelay(retryPolicy, retryCount);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // Max retries exceeded
                await _jobQueue.FailAsync(job.JobId, ex, cancellationToken);
                return;
            }
        }
    }
}
```

---

## 3. Memory-Aware Rebalancing

### Memory Monitoring Implementation

```csharp
public class MemoryMonitor : IMemoryMonitor
{
    private readonly ConcurrentDictionary<string, long> _actorMemoryUsage = new();
    private readonly ILogger<MemoryMonitor> _logger;
    
    public long GetActorMemoryUsage(string actorId)
    {
        return _actorMemoryUsage.GetValueOrDefault(actorId, 0L);
    }
    
    public MemoryMetrics GetSiloMemoryMetrics()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var process = Process.GetCurrentProcess();
        
        return new MemoryMetrics
        {
            TotalMemoryBytes = gcInfo.HeapSizeBytes,
            AvailableMemoryBytes = gcInfo.TotalAvailableMemoryBytes - gcInfo.HeapSizeBytes,
            MemoryPressure = CalculateMemoryPressure(gcInfo),
            Gen0Collections = GC.CollectionCount(0),
            Gen2Collections = GC.CollectionCount(2),
            LastGCPause = gcInfo.PauseDurations.Length > 0 ? 
                         gcInfo.PauseDurations[^1] : TimeSpan.Zero
        };
    }
    
    public void RecordActorAllocation(string actorId, long bytes)
    {
        _actorMemoryUsage.AddOrUpdate(actorId, bytes, (_, current) => current + bytes);
    }
    
    public void RecordActorDeallocation(string actorId, long bytes)
    {
        _actorMemoryUsage.AddOrUpdate(actorId, -bytes, (_, current) => Math.Max(0, current - bytes));
    }
    
    private static double CalculateMemoryPressure(GCMemoryInfo gcInfo)
    {
        var heapSize = gcInfo.HeapSizeBytes;
        var totalAvailable = gcInfo.TotalAvailableMemoryBytes;
        
        if (totalAvailable == 0)
            return 0.0;
        
        // Pressure = heap / available (0.0 to 1.0+)
        return Math.Min(1.0, (double)heapSize / totalAvailable);
    }
}
```

### Memory-Aware Placement Policy

```csharp
public class MemoryAwarePlacementPolicy : IPlacementPolicy
{
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly MemoryAwarePlacementOptions _options;
    
    public async Task<string> SelectSiloAsync(
        ActorPlacementContext context,
        CancellationToken cancellationToken = default)
    {
        var candidateSilos = context.AvailableSilos;
        
        // Filter out silos with critical memory pressure
        var viableSilos = new List<string>();
        
        foreach (var siloId in candidateSilos)
        {
            var metrics = await GetSiloMemoryMetricsAsync(siloId);
            
            // Reject if memory pressure is critical
            if (metrics.MemoryPressure >= _options.CriticalThreshold)
            {
                _logger.LogWarning(
                    "Silo {SiloId} has critical memory pressure {Pressure:P2}, excluding from placement",
                    siloId, metrics.MemoryPressure);
                continue;
            }
            
            viableSilos.Add(siloId);
        }
        
        if (viableSilos.Count == 0)
        {
            throw new InvalidOperationException(
                "No silos available with acceptable memory pressure");
        }
        
        // Select silo with lowest memory usage
        var selectedSilo = viableSilos[0];
        var lowestPressure = double.MaxValue;
        
        foreach (var siloId in viableSilos)
        {
            var metrics = await GetSiloMemoryMetricsAsync(siloId);
            if (metrics.MemoryPressure < lowestPressure)
            {
                lowestPressure = metrics.MemoryPressure;
                selectedSilo = siloId;
            }
        }
        
        return selectedSilo;
    }
}
```

---

## 4. Inbox/Outbox Pattern

### Outbox Implementation with Transaction

```csharp
public class PostgresOutbox : IOutbox
{
    private readonly NpgsqlDataSource _dataSource;
    
    public async Task EnqueueAsync(
        OutboxMessage message,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO outbox (message_id, destination, payload, created_at, retry_count)
            VALUES ($1, $2, $3, $4, $5)";
        
        NpgsqlConnection? connection = null;
        var shouldDisposeConnection = false;
        
        try
        {
            if (transaction != null)
            {
                connection = transaction.Connection;
            }
            else
            {
                connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                shouldDisposeConnection = true;
            }
            
            await using var cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue(message.MessageId);
            cmd.Parameters.AddWithValue(message.Destination);
            cmd.Parameters.AddWithValue(JsonSerializer.Serialize(message.Payload));
            cmd.Parameters.AddWithValue(message.CreatedAt);
            cmd.Parameters.AddWithValue(message.RetryCount);
            
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldDisposeConnection)
                await connection!.DisposeAsync();
        }
    }
    
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT message_id, destination, payload, created_at, retry_count
            FROM outbox
            WHERE sent_at IS NULL
            ORDER BY created_at
            LIMIT $1
            FOR UPDATE SKIP LOCKED";
        
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue(batchSize);
        
        var messages = new List<OutboxMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessage
            {
                MessageId = reader.GetString(0),
                Destination = reader.GetString(1),
                Payload = JsonSerializer.Deserialize<object>(reader.GetString(2))!,
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(3),
                RetryCount = reader.GetInt32(4)
            });
        }
        
        return messages;
    }
}
```

### Inbox Deduplication

```csharp
public class RedisInbox : IInbox
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _retentionPeriod;
    
    public async Task<bool> IsProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"inbox:processed:{messageId}";
        
        return await db.KeyExistsAsync(key);
    }
    
    public async Task MarkAsProcessedAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var key = $"inbox:processed:{messageId}";
        
        // Set with TTL for automatic cleanup
        await db.StringSetAsync(key, DateTimeOffset.UtcNow.Ticks, _retentionPeriod);
    }
    
    public async Task CleanupOldEntriesAsync(
        TimeSpan retentionPeriod,
        CancellationToken cancellationToken = default)
    {
        // Redis TTL handles cleanup automatically
        await Task.CompletedTask;
    }
}
```

### Actor with Transactional Messaging

```csharp
[Actor(Name = "Order")]
public class OrderActor : StatefulActorBase<OrderState>
{
    private readonly IOutbox _outbox;
    private readonly IInbox _inbox;
    
    public async Task ProcessOrderAsync(OrderMessage message)
    {
        // Inbox: Check for duplicate
        if (await _inbox.IsProcessedAsync(message.MessageId))
        {
            _logger.LogInformation("Duplicate message {MessageId}, skipping", message.MessageId);
            return;
        }
        
        // Begin transaction for state + outbox
        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            // Update state
            State.OrderId = message.OrderId;
            State.Status = OrderStatus.Processing;
            await SaveStateAsync(connection, transaction);
            
            // Enqueue outgoing message (same transaction)
            await _outbox.EnqueueAsync(new OutboxMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Destination = "PaymentService",
                Payload = new ProcessPaymentCommand { OrderId = message.OrderId }
            }, transaction);
            
            // Mark as processed
            await transaction.CommitAsync();
            await _inbox.MarkAsProcessedAsync(message.MessageId);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
```

---

## Testing Patterns

### Event Store Integration Test

```csharp
public class PostgresEventStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlDataSource _dataSource;
    private PostgresEventStore _eventStore;
    
    public PostgresEventStoreTests()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
    }
    
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        
        var connectionString = _container.GetConnectionString();
        _dataSource = NpgsqlDataSource.Create(connectionString);
        
        // Create schema
        await CreateSchemaAsync();
        
        _eventStore = new PostgresEventStore(_dataSource, new JsonSerializerOptions());
    }
    
    [Fact]
    public async Task AppendEvents_ConcurrencyCheck_ThrowsOnMismatch()
    {
        // Arrange
        var actorId = "test-actor";
        var events = new[] { new TestEvent { Data = "Event 1" } };
        
        // Act: Append first time
        var version1 = await _eventStore.AppendEventsAsync(actorId, events);
        
        // Assert: Appending with wrong expected version throws
        await Assert.ThrowsAsync<EventStoreConcurrencyException>(() =>
            _eventStore.AppendEventsAsync(actorId, events, expectedVersion: 0));
    }
    
    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}
```

---

## Performance Guidelines

### Benchmarking Template

```csharp
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class EventStoreBenchmarks
{
    private IEventStore _eventStore;
    private List<DomainEvent> _events;
    
    [GlobalSetup]
    public void Setup()
    {
        _eventStore = new InMemoryEventStore();
        _events = Enumerable.Range(0, 100)
            .Select(i => new TestEvent { Data = $"Event {i}" })
            .ToList<DomainEvent>();
    }
    
    [Benchmark]
    public async Task AppendEvents_100Events()
    {
        await _eventStore.AppendEventsAsync("actor-1", _events);
    }
    
    [Benchmark]
    public async Task ReadEvents_100Events()
    {
        await _eventStore.ReadEventsAsync("actor-1", fromVersion: 0);
    }
}
```

### Performance Targets

| Feature | Metric | Target | Measurement |
|---------|--------|--------|-------------|
| Event Store Write | Latency (p99) | <20ms | Single event append to Postgres |
| Event Store Read | Latency (p99) | <10ms | Read 100 events |
| Job Queue Enqueue | Throughput | >5000/sec | Redis queue |
| Job Queue Dequeue | Latency | <5ms | Single job |
| Memory Monitoring | Overhead | <2% CPU | Continuous monitoring |
| Inbox Check | Latency | <1ms | Redis lookup |

---

## Documentation Requirements

### API Documentation Template

```csharp
/// <summary>
/// Stores and retrieves domain events for event sourcing.
/// </summary>
/// <remarks>
/// This implementation uses PostgreSQL for persistent storage with optimistic concurrency control.
/// Events are stored in append-only fashion and can be replayed to reconstruct actor state.
/// </remarks>
/// <example>
/// <code>
/// var eventStore = new PostgresEventStore(dataSource);
/// 
/// // Append events
/// var events = new[] { new OrderPlacedEvent { OrderId = "123" } };
/// var version = await eventStore.AppendEventsAsync("order-123", events);
/// 
/// // Read events
/// var history = await eventStore.ReadEventsAsync("order-123");
/// </code>
/// </example>
public class PostgresEventStore : IEventStore
{
    /// <summary>
    /// Appends one or more events to an actor's event stream with optimistic concurrency control.
    /// </summary>
    /// <param name="actorId">The unique identifier of the actor.</param>
    /// <param name="events">The events to append.</param>
    /// <param name="expectedVersion">
    /// The expected current version before appending. If specified and doesn't match,
    /// throws <see cref="EventStoreConcurrencyException"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new version after appending.</returns>
    /// <exception cref="EventStoreConcurrencyException">
    /// Thrown when <paramref name="expectedVersion"/> doesn't match the actual version.
    /// </exception>
    public async Task<long> AppendEventsAsync(
        string actorId,
        IReadOnlyList<DomainEvent> events,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        // Implementation...
    }
}
```

---

## Common Pitfalls

### ❌ Pitfall 1: Runtime Reflection

```csharp
// BAD: Runtime type lookup
var eventType = Type.GetType(eventTypeName);
var eventInstance = Activator.CreateInstance(eventType);

// GOOD: Static factory with source generation
[ModuleInitializer]
public static void RegisterEventFactories()
{
    EventFactory.Register<OrderPlacedEvent>("OrderPlaced");
    EventFactory.Register<OrderShippedEvent>("OrderShipped");
}
```

### ❌ Pitfall 2: Unbounded Memory Growth

```csharp
// BAD: Unbounded cache
private readonly Dictionary<string, Actor> _actorCache = new();

// GOOD: LRU cache with size limit
private readonly LruCache<string, Actor> _actorCache = new(maxSize: 10000);
```

### ❌ Pitfall 3: Blocking Async Code

```csharp
// BAD: Blocking on async
var result = GetDataAsync().Result;

// GOOD: Await properly
var result = await GetDataAsync();

// GOOD: ValueTask for hot paths
public ValueTask<int> GetCountAsync()
{
    if (_cached) return new ValueTask<int>(_count);
    return new ValueTask<int>(LoadCountAsync());
}
```

---

## Checklist for Feature Completion

Before marking a feature as complete:

- [ ] **Code Quality**
  - [ ] No CodeQL high/critical alerts
  - [ ] Follows existing code conventions
  - [ ] XML documentation for public APIs
  - [ ] No compiler warnings

- [ ] **AOT Compatibility**
  - [ ] Builds with `PublishAot=true`
  - [ ] No reflection in hot paths
  - [ ] Source generators for dynamic code

- [ ] **Testing**
  - [ ] Unit tests (>85% coverage)
  - [ ] Integration tests with Testcontainers
  - [ ] Performance benchmarks
  - [ ] Example application

- [ ] **Documentation**
  - [ ] API documentation
  - [ ] Architecture decision record (ADR)
  - [ ] Integration guide
  - [ ] Migration guide (if applicable)

- [ ] **Performance**
  - [ ] Meets performance targets
  - [ ] No memory leaks
  - [ ] Benchmarks added

---

## Resources

- **AOT Guidelines:** `docs/ZERO_REFLECTION_ACHIEVEMENT.md`
- **Source Generators:** `docs/SOURCE_GENERATOR_SETUP.md`
- **Testing Patterns:** `tests/Quark.Tests/`
- **Example Apps:** `examples/`

---

**Next Steps:**
1. Review this guide with team
2. Select first feature to implement
3. Create feature branch
4. Follow implementation patterns
5. Submit PR with checklist complete
