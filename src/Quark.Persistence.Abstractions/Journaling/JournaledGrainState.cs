namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Shell-owned state for a <see cref="JournaledGrain{TState,TEvent}" /> activation.
///     Held in the memory bag via <c>IActivationMemory&lt;JournaledGrainState&lt;TState, TEvent&gt;&gt;</c>.
/// </summary>
public sealed class JournaledGrainState<TState, TEvent>
    where TState : class, new()
{
    public TState State { get; set; } = new();
    public List<TEvent> StagedEvents { get; } = [];
    public int ConfirmedVersion { get; set; }

    /// <summary>The <see cref="ConfirmedVersion" /> captured by the most recent snapshot write.</summary>
    public int LastSnapshotVersion { get; set; }
}
