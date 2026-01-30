using Quark.Abstractions;
using Quark.Core.Actors;
using System.Security.Cryptography;
using System.Text;

namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Stateless worker for processing image transformation requests.
/// Multiple instances can run concurrently for high-throughput processing.
/// </summary>
[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public ImageProcessorActor(string actorId) : base(actorId)
    {
    }

    public ImageProcessorActor(string actorId, IActorFactory? actorFactory) : base(actorId, actorFactory)
    {
    }

    /// <summary>
    /// Simulates image resizing operation (stateless computation).
    /// In a real implementation, this would use an image processing library.
    /// </summary>
    public async Task<ImageResult> ResizeImageAsync(byte[] imageData, int targetWidth, int targetHeight)
    {
        // Simulate CPU-intensive image processing
        await Task.Delay(50); // Simulate processing time

        // For demonstration, we'll just compute a hash of the input
        // In a real implementation, this would use an actual image processing library
        var hash = ComputeHash(imageData);
        var processedSize = imageData.Length / 2; // Simulated compression

        return new ImageResult
        {
            Width = targetWidth,
            Height = targetHeight,
            SizeBytes = processedSize,
            ProcessedAt = DateTime.UtcNow,
            Hash = hash,
            ProcessedBy = ActorId
        };
    }

    /// <summary>
    /// Applies a filter to an image (stateless computation).
    /// </summary>
    public async Task<ImageResult> ApplyFilterAsync(byte[] imageData, string filterType)
    {
        // Simulate filter application
        await Task.Delay(30);

        var hash = ComputeHash(imageData);

        return new ImageResult
        {
            Width = 0, // Would be determined from actual image
            Height = 0,
            SizeBytes = imageData.Length,
            ProcessedAt = DateTime.UtcNow,
            Hash = hash,
            ProcessedBy = ActorId,
            FilterApplied = filterType
        };
    }

    /// <summary>
    /// Validates image data format (lightweight stateless operation).
    /// </summary>
    public Task<ValidationResult> ValidateImageAsync(byte[] imageData)
    {
        var isValid = imageData.Length > 0 && imageData.Length < 10 * 1024 * 1024; // Max 10MB
        var message = isValid ? "Valid image" : "Invalid image size";

        return Task.FromResult(new ValidationResult
        {
            IsValid = isValid,
            Message = message,
            ValidatedBy = ActorId
        });
    }

    private static string ComputeHash(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash)[..16]; // First 16 chars for brevity
    }
}

/// <summary>
/// Result of image processing operation.
/// </summary>
public record ImageResult
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int SizeBytes { get; init; }
    public DateTime ProcessedAt { get; init; }
    public string Hash { get; init; } = string.Empty;
    public string ProcessedBy { get; init; } = string.Empty;
    public string? FilterApplied { get; init; }
}

/// <summary>
/// Result of validation operation.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ValidatedBy { get; init; } = string.Empty;
}
