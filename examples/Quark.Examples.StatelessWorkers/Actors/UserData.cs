namespace Quark.Examples.StatelessWorkers.Actors;

/// <summary>
/// Input data for validation.
/// </summary>
public record UserData
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public int Age { get; init; }
}