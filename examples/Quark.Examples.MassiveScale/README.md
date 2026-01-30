# Quark Phase 8.3: Massive Scale Support Example

This example demonstrates the massive scale support features introduced in Phase 8.3:

## Features Demonstrated

### 1. Hierarchical Consistent Hashing
- **3-tier hash ring**: Region → Zone → Silo
- **Geo-aware placement**: Actors placed in preferred regions/zones
- **Shard groups**: Logical grouping for very large clusters
- **Lock-free reads**: High-performance concurrent access

### 2. Adaptive Mailbox Sizing
- **Dynamic capacity**: Automatically adjusts based on load
- **Burst handling**: Grows during traffic spikes
- **Memory efficiency**: Shrinks during low utilization
- **Configurable thresholds**: Customizable grow/shrink behavior

### 3. Circuit Breaker
- **Fault isolation**: Prevents cascading failures
- **Three states**: Closed, Open, Half-Open
- **Automatic recovery**: Transitions based on success/failure counts
- **Configurable timeouts**: Customizable recovery periods

### 4. Rate Limiting
- **Traffic control**: Limits messages per time window
- **Multiple actions**: Drop, Reject, or Queue excess messages
- **Per-actor limits**: Independent rate limiting per actor
- **Sliding window**: Accurate rate tracking

## Running the Example

```bash
cd examples/Quark.Examples.MassiveScale
dotnet run
```

## Example Output

The example prints detailed information about each feature, including:
- Cluster topology (regions, zones, silos)
- Geo-aware actor placement decisions
- Adaptive mailbox configuration and behavior
- Circuit breaker state transitions
- Rate limiting policies and actions

## Key Takeaways

1. **Scalability**: Hierarchical hashing enables clusters with 1000+ silos
2. **Reliability**: Circuit breakers prevent cascading failures
3. **Performance**: Adaptive mailboxes handle burst traffic efficiently
4. **Control**: Rate limiting protects actors from overload

## Related Documentation

- [ENHANCEMENTS.md](../../docs/ENHANCEMENTS.md) - Phase 8.3 specification
- [API Documentation](../../docs/) - Detailed API reference
- [Architecture Guide](../../docs/) - System architecture overview
