namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Result of validation operation.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ValidatedBy { get; init; } = string.Empty;
}