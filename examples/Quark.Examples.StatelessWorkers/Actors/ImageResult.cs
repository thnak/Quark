namespace Quark.Examples.StatelessWorkers.Actors;

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