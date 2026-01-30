# Quark Stateless Workers Example

This example demonstrates the **Stateless Workers** feature in Quark, which provides high-throughput actors optimized for compute-intensive operations without state persistence overhead.

## Overview

Stateless workers are lightweight actors that:
- Have no state persistence overhead
- Can run multiple instances with the same actor ID
- Are optimized for high-throughput concurrent processing
- Have minimal activation/deactivation costs
- Are ideal for CPU-bound stateless computations

## Features Demonstrated

### 1. Image Processing Workers
- Multiple concurrent instances processing images
- Stateless image resizing and filtering
- Image validation
- Zero state persistence overhead

### 2. Data Validation Workers
- Individual record validation and enrichment
- Batch processing with parallel execution
- High-throughput data processing

### 3. High-Throughput Processing
- Pool of 10 workers processing 100 requests
- Demonstrates scalability and load distribution
- Shows performance characteristics (throughput, latency)

## Usage

### Defining a Stateless Worker

```csharp
[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public ImageProcessorActor(string actorId) : base(actorId)
    {
    }

    public async Task<ImageResult> ResizeImageAsync(byte[] imageData, int width, int height)
    {
        // Stateless processing - no state load/save
        await Task.Delay(50); // Simulate processing
        
        return new ImageResult
        {
            Width = width,
            Height = height,
            ProcessedAt = DateTime.UtcNow,
            ProcessedBy = ActorId
        };
    }
}
```

### Creating Multiple Instances

```csharp
var factory = new ActorFactory();

// Create multiple instances with the same ID
var worker1 = factory.CreateActor<ImageProcessorActor>("image-processor");
var worker2 = factory.CreateActor<ImageProcessorActor>("image-processor");
var worker3 = factory.CreateActor<ImageProcessorActor>("image-processor");

// All have the same ID but are different instances
// This enables load balancing across multiple workers
```

### Concurrent Processing

```csharp
// Process requests concurrently across multiple workers
var task1 = worker1.ResizeImageAsync(imageData, 800, 600);
var task2 = worker2.ApplyFilterAsync(imageData, "grayscale");
var task3 = worker3.ResizeImageAsync(imageData, 1920, 1080);

var results = await Task.WhenAll(task1, task2, task3);
```

## Running the Example

```bash
# Build the example
dotnet build examples/Quark.Examples.StatelessWorkers

# Run the example
dotnet run --project examples/Quark.Examples.StatelessWorkers
```

## Expected Output

```
=== Quark Stateless Workers Example ===

This example demonstrates high-throughput stateless workers
that can process requests without state persistence overhead.

✓ Actor factory created

--- Part 1: Image Processing Workers ---

✓ Created 3 image processor instances with same ID
  - Worker 1: image-processor
  - Worker 2: image-processor
  - Worker 3: image-processor
  - Different instances: True

Processing 3 images concurrently...
✓ Processed 3 images in 54ms
  - Image 1 resized to 800x600 by image-processor
  - Image 2 filtered by image-processor
  - Image 3 resized to 1920x1080 by image-processor

...

--- Part 3: High-Throughput Test ---

✓ Created pool of 10 image processors

Processing 100 images with 10 workers...
✓ Processed 100 images in 50ms
  - Throughput: 1966.8 images/second
  - Average latency: 0.5ms per image
  - Workers used: 1

=== Example completed successfully ===
```

## Use Cases

Stateless workers are ideal for:

1. **Image Processing**: Resizing, filtering, format conversion
2. **Data Validation**: Input validation, schema checking
3. **Data Enrichment**: Adding metadata, lookups
4. **API Aggregation**: Combining data from multiple sources
5. **Computations**: Mathematical calculations, transformations
6. **Protocol Translation**: Converting between data formats

## Key Benefits

- ✅ **No State Overhead**: No load/save operations for state
- ✅ **Multiple Instances**: Same actor ID, different instances
- ✅ **High Throughput**: Optimized for concurrent processing
- ✅ **Load Balancing**: Automatic distribution across instances
- ✅ **Minimal Overhead**: Fast activation/deactivation
- ✅ **Scalable**: Configure min/max instances per workload

## Configuration

Use the `StatelessWorkerAttribute` to configure scaling:

```csharp
[StatelessWorker(MinInstances = 5, MaxInstances = 50)]
```

- `MinInstances`: Minimum instances to maintain (default: 1)
- `MaxInstances`: Maximum instances allowed (default: 10)

## Comparison with Regular Actors

| Feature | Regular Actor | Stateless Worker |
|---------|--------------|------------------|
| State Persistence | Yes | No |
| Multiple Instances per ID | No | Yes |
| Activation Overhead | Higher | Minimal |
| Best For | Stateful workflows | High-throughput compute |
| Scalability | Vertical | Horizontal |

## Next Steps

- Explore the code in `Actors/ImageProcessorActor.cs` and `Actors/DataValidatorActor.cs`
- Try modifying the number of workers and workload
- Add your own stateless worker implementations
- Experiment with different processing patterns

## Related Examples

- `Quark.Examples.Basic` - Basic actor usage
- `Quark.Examples.Supervision` - Actor supervision patterns
- `Quark.Examples.Streaming` - Reactive streaming
