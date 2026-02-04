namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Result of batch validation.
/// </summary>
public record BatchValidationResult
{
    public int TotalRecords { get; init; }
    public int ValidRecords { get; init; }
    public int InvalidRecords { get; init; }
    public List<EnrichedData> Results { get; init; } = new();
    public string ProcessedBy { get; init; } = string.Empty;
    public DateTime ProcessedAt { get; init; }
}