namespace Quark.Abstractions.Persistence;

/// <summary>
///     Wrapper for state with version information for optimistic concurrency control.
/// </summary>
/// <typeparam name="TState">The type of state.</typeparam>
public sealed class StateWithVersion<TState> where TState : class
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="StateWithVersion{TState}"/> class.
    /// </summary>
    /// <param name="state">The state data.</param>
    /// <param name="version">The version number (E-Tag).</param>
    public StateWithVersion(TState state, long version)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Version = version;
    }

    /// <summary>
    ///     Gets the state data.
    /// </summary>
    public TState State { get; }

    /// <summary>
    ///     Gets the version number (E-Tag) for optimistic concurrency control.
    /// </summary>
    public long Version { get; }
}
