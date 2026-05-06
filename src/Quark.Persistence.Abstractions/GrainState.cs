namespace Quark.Persistence.Abstractions;

/// <summary>
/// Mutable container holding a grain's persisted state snapshot and metadata.
/// Modeled after Orleans' <c>GrainState&lt;T&gt;</c> contract.
/// </summary>
public sealed class GrainState<TState> where TState : new()
{
    /// <summary>The current state value.</summary>
    public TState State { get; set; } = new();

    /// <summary>Opaque version/ETag token for concurrency-aware providers.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Whether a backing record existed when the state was read.</summary>
    public bool RecordExists { get; set; }
}