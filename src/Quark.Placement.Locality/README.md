# Quark.Placement.Locality

Locality-aware actor placement strategy for Quark Framework.

## Overview

This package provides intelligent actor placement that minimizes cross-silo network traffic by analyzing communication patterns and co-locating frequently communicating actors.

## Features

- **Communication Pattern Analysis**: Tracks message exchanges between actors
- **Smart Co-location**: Places actors that communicate frequently on the same silo
- **Configurable Thresholds**: Control when actors are considered "hot" pairs
- **Automatic Cleanup**: Removes old communication data to prevent memory bloat
- **Load Balancing**: Balances locality optimization with even load distribution

## Usage

```csharp
// Register services
services.AddSingleton<ICommunicationPatternAnalyzer, CommunicationPatternAnalyzer>();
services.Configure<LocalityAwarePlacementOptions>(options =>
{
    options.AnalysisWindow = TimeSpan.FromMinutes(5);
    options.HotPairThreshold = 100;
    options.LocalityWeight = 0.7;  // 70% locality, 30% load balance
});
services.AddSingleton<IPlacementPolicy, LocalityAwarePlacementPolicy>();
```

## Configuration Options

- **AnalysisWindow**: Time window for analyzing communication patterns (default: 5 minutes)
- **HotPairThreshold**: Minimum message count to consider actors as frequently communicating (default: 100)
- **LocalityWeight**: Weight for locality vs load balancing (0.0-1.0, default: 0.7)
- **CleanupInterval**: How often to clean up old data (default: 10 minutes)
- **MaxDataAge**: Maximum age of communication data to retain (default: 30 minutes)

## How It Works

1. The `CommunicationPatternAnalyzer` tracks all actor-to-actor messages
2. When placing a new actor, `LocalityAwarePlacementPolicy` examines communication patterns
3. It scores each silo based on how many frequently-communicating actors are already there
4. The actor is placed on the silo with the highest score
5. If no communication patterns exist, it falls back to random load balancing

## Performance Considerations

- Uses `ConcurrentDictionary` for thread-safe tracking
- Periodic cleanup prevents unbounded memory growth
- Cached placement decisions avoid repeated graph traversals
- Analysis window limits the amount of data processed

## AOT Compatibility

✅ Fully compatible with Native AOT compilation. No reflection or dynamic code generation.
