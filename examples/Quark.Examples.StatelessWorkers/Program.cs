using Quark.Core.Actors;
using Quark.Examples.StatelessWorkers.Actors;
using System.Diagnostics;
using System.Text;

Console.WriteLine("=== Quark Stateless Workers Example ===");
Console.WriteLine();
Console.WriteLine("This example demonstrates high-throughput stateless workers");
Console.WriteLine("that can process requests without state persistence overhead.");
Console.WriteLine();

// Create an actor factory
var factory = new ActorFactory();
Console.WriteLine("✓ Actor factory created");
Console.WriteLine();

// ==========================================
// Part 1: Image Processing Workers
// ==========================================
Console.WriteLine("--- Part 1: Image Processing Workers ---");
Console.WriteLine();

// Create multiple instances of the same stateless worker
var imageWorker1 = factory.CreateActor<ImageProcessorActor>("image-processor");
var imageWorker2 = factory.CreateActor<ImageProcessorActor>("image-processor");
var imageWorker3 = factory.CreateActor<ImageProcessorActor>("image-processor");

Console.WriteLine($"✓ Created 3 image processor instances with same ID");
Console.WriteLine($"  - Worker 1: {imageWorker1.ActorId}");
Console.WriteLine($"  - Worker 2: {imageWorker2.ActorId}");
Console.WriteLine($"  - Worker 3: {imageWorker3.ActorId}");
Console.WriteLine($"  - Different instances: {!ReferenceEquals(imageWorker1, imageWorker2)}");
Console.WriteLine();

// Simulate image data
var imageData = Encoding.UTF8.GetBytes("This is simulated image data with some content to process");

// Process images concurrently across multiple workers
Console.WriteLine("Processing 3 images concurrently...");
var sw = Stopwatch.StartNew();

var task1 = imageWorker1.ResizeImageAsync(imageData, 800, 600);
var task2 = imageWorker2.ApplyFilterAsync(imageData, "grayscale");
var task3 = imageWorker3.ResizeImageAsync(imageData, 1920, 1080);

var results = await Task.WhenAll(task1, task2, task3);
sw.Stop();

Console.WriteLine($"✓ Processed 3 images in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  - Image 1 resized to 800x600 by {results[0].ProcessedBy}");
Console.WriteLine($"  - Image 2 filtered by {results[1].ProcessedBy}");
Console.WriteLine($"  - Image 3 resized to 1920x1080 by {results[2].ProcessedBy}");
Console.WriteLine();

// Validate an image
var validationResult = await imageWorker1.ValidateImageAsync(imageData);
Console.WriteLine($"✓ Image validation: {validationResult.Message}");
Console.WriteLine($"  - Validated by: {validationResult.ValidatedBy}");
Console.WriteLine();

// ==========================================
// Part 2: Data Validation Workers
// ==========================================
Console.WriteLine("--- Part 2: Data Validation Workers ---");
Console.WriteLine();

// Create data validator workers
var validator1 = factory.CreateActor<DataValidatorActor>("data-validator");
var validator2 = factory.CreateActor<DataValidatorActor>("data-validator");

Console.WriteLine($"✓ Created 2 data validator instances");
Console.WriteLine();

// Process individual records
var userData1 = new UserData { Name = "Alice", Email = "alice@example.com", Age = 30 };
var userData2 = new UserData { Name = "Bob", Email = "invalid-email", Age = 25 };

Console.WriteLine("Processing individual records...");
var enriched1 = await validator1.EnrichUserDataAsync(userData1);
var enriched2 = await validator2.EnrichUserDataAsync(userData2);

Console.WriteLine($"✓ Record 1: {(enriched1.IsValid ? "VALID" : "INVALID")} - processed by {enriched1.ProcessedBy}");
if (enriched1.IsValid)
{
    Console.WriteLine($"  - Email domain: {enriched1.Metadata["email_domain"]}");
}

Console.WriteLine($"✓ Record 2: {(enriched2.IsValid ? "INVALID" : "VALID")} - processed by {enriched2.ProcessedBy}");
if (!enriched2.IsValid)
{
    Console.WriteLine($"  - Errors: {string.Join(", ", enriched2.ValidationErrors)}");
}
Console.WriteLine();

// Process batch of records
Console.WriteLine("Processing batch of 5 records...");
var batch = new List<UserData>
{
    new() { Name = "Charlie", Email = "charlie@example.com", Age = 35 },
    new() { Name = "Diana", Email = "diana@example.com", Age = 28 },
    new() { Name = "Eve", Email = "", Age = 22 }, // Invalid
    new() { Name = "", Email = "frank@example.com", Age = 40 }, // Invalid
    new() { Name = "Grace", Email = "grace@example.com", Age = 33 }
};

sw.Restart();
var batchResult = await validator1.ValidateBatchAsync(batch);
sw.Stop();

Console.WriteLine($"✓ Batch processed in {sw.ElapsedMilliseconds}ms by {batchResult.ProcessedBy}");
Console.WriteLine($"  - Total records: {batchResult.TotalRecords}");
Console.WriteLine($"  - Valid: {batchResult.ValidRecords}");
Console.WriteLine($"  - Invalid: {batchResult.InvalidRecords}");
Console.WriteLine();

// ==========================================
// Part 3: High-Throughput Demonstration
// ==========================================
Console.WriteLine("--- Part 3: High-Throughput Test ---");
Console.WriteLine();

// Create more workers for high-throughput test
var workers = Enumerable.Range(0, 10)
    .Select(_ => factory.CreateActor<ImageProcessorActor>("image-processor-pool"))
    .ToList();

Console.WriteLine($"✓ Created pool of {workers.Count} image processors");
Console.WriteLine();

// Process many images concurrently
Console.WriteLine("Processing 100 images with 10 workers...");
sw.Restart();

var tasks = Enumerable.Range(0, 100)
    .Select(i => workers[i % workers.Count].ResizeImageAsync(imageData, 1024, 768))
    .ToArray();

var allResults = await Task.WhenAll(tasks);
sw.Stop();

Console.WriteLine($"✓ Processed 100 images in {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"  - Throughput: {100.0 / sw.Elapsed.TotalSeconds:F1} images/second");
Console.WriteLine($"  - Average latency: {sw.ElapsedMilliseconds / 100.0:F1}ms per image");

// Count how many workers actually processed images
var workerUsage = allResults.GroupBy(r => r.ProcessedBy).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"  - Workers used: {workerUsage.Count}");
Console.WriteLine();

Console.WriteLine("=== Example completed successfully ===");
Console.WriteLine();
Console.WriteLine("Key benefits of Stateless Workers:");
Console.WriteLine("  ✓ No state persistence overhead");
Console.WriteLine("  ✓ Multiple instances per actor ID");
Console.WriteLine("  ✓ High-throughput concurrent processing");
Console.WriteLine("  ✓ Automatic load distribution");
Console.WriteLine("  ✓ Minimal activation/deactivation cost");
