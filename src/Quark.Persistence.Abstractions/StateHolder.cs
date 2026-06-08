namespace Quark.Persistence.Abstractions;

/// <summary>
///     Engine-internal. One instance per (GrainActivation, TState) pair.
///     Owned by the shell's memory bag; shared between IActivationMemory&lt;TState&gt; and
///     IPersistentActivationMemory&lt;TState&gt; accessors so mutations from either are immediately visible.
/// </summary>
public sealed class StateHolder<TState> where TState : class, new()
{
    public TState Value { get; set; } = new();
}
