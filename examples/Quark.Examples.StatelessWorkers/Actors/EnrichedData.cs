namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Enriched and validated data.
/// </summary>
public record EnrichedData
{
    public UserData? OriginalData { get; init; }
    public bool IsValid { get; init; }
    public List<string> ValidationErrors { get; init; } = new();
    public DateTime EnrichedAt { get; init; }
    public string ProcessedBy { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
}