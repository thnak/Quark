# Quark Backpressure & Flow Control Example

This example demonstrates the backpressure and flow control features in Quark streaming (Phase 8.5).

## What it Demonstrates

Five different backpressure strategies:

1. **No Backpressure**: Default mode, messages delivered immediately
2. **DropOldest**: Drops oldest messages when buffer is full (good for real-time data)
3. **DropNewest**: Drops newest messages when buffer is full (good for FIFO queues)
4. **Block**: Blocks publishers until space is available (guaranteed delivery)
5. **Throttle**: Rate-limits message publishing (good for API rate limits)

## Running the Example

```bash
cd examples/Quark.Examples.Backpressure
dotnet run
```

## Expected Output

The example will show metrics for each strategy:
- Messages published
- Messages dropped (if any)
- Messages received by consumers
- Additional strategy-specific metrics

## Key Concepts

- **Buffer Size**: Controls how many pending messages can be queued
- **Slow Consumers**: Simulated with Task.Delay to show backpressure in action
- **Metrics**: Real-time visibility into flow control effectiveness

## Use Cases

- **Sensor Data**: Use DropOldest to always get latest values
- **Order Processing**: Use DropNewest or Block to preserve order priority
- **Notifications**: Use Throttle to respect rate limits
- **Critical Transactions**: Use Block for guaranteed delivery
