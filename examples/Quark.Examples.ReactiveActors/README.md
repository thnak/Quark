# Quark Reactive Actors Example

This example demonstrates **Phase 10.1.3: Reactive Actors** with backpressure-aware stream processing.

## Features Demonstrated

### 1. Stream Aggregation with Time Windows
- Aggregates sensor readings in 2-second time windows
- Calculates statistics (average, min, max temperature)
- Shows how time-based windowing works in practice

### 2. Stream Operators
- **Map**: Transform values (e.g., multiply by 2)
- **Filter**: Select values meeting criteria
- Demonstrates operator chaining

### 3. Windowing Strategies
- **Count-based windows**: Batch processing (5 items per window)
- **Time-based windows**: Time-interval aggregation
- Shows different windowing approaches

## Key Concepts

### ReactiveActorBase<TIn, TOut>
Base class for reactive actors that:
- Process async streams with built-in backpressure
- Support configurable buffer sizes and overflow strategies
- Provide metrics (messages received, processed, dropped)
- Integrate with System.Threading.Channels for flow control

### Windowing Extensions
- `Window(TimeSpan)`: Time-based windows
- `Window(int)`: Count-based windows
- `SlidingWindow(windowSize, slide)`: Overlapping windows
- `SessionWindow(inactivityGap)`: Event correlation windows

### Stream Operators
- `Map<TSource, TResult>`: Transform each element
- `MapAsync<TSource, TResult>`: Async transformation
- `Filter<T>`: Select elements matching a predicate
- `FilterAsync<T>`: Async filtering
- `Reduce<TSource, TAccumulate>`: Aggregate all elements
- `GroupByStream<TSource, TKey>`: Group by key

## Running the Example

```bash
cd examples/Quark.Examples.ReactiveActors
dotnet run
```

## Configuration

Actors can be configured with the `[ReactiveActor]` attribute:

```csharp
[ReactiveActor(
    BufferSize = 1000,              // Input buffer size
    BackpressureThreshold = 0.8,    // When to signal backpressure (80%)
    OverflowStrategy = BackpressureMode.Block,  // How to handle overflow
    EnableMetrics = true            // Track message metrics
)]
```

## Overflow Strategies

- **Block**: Wait for buffer space (guarantees delivery)
- **DropOldest**: Drop oldest messages when full
- **DropNewest**: Drop newest messages when full
- **Throttle**: Rate-limit message publishing

## Use Cases

- **Real-time analytics**: Aggregate streaming data with windows
- **Event processing**: Transform and filter event streams
- **Data pipelines**: Chain operators for ETL workflows
- **Rate limiting**: Control flow with backpressure
