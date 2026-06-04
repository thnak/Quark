namespace Quark.Persistence.Abstractions;

/// <summary>
///     A named, independently readable and writable state slot injected into a grain constructor.
///     Orleans-compatible: use with <c>[PersistentState("name", "provider")]</c>.
/// </summary>
public interface IPersistentState<TState>
    where TState : new()
{
    /// <summary>Gets or sets the current state value.</summary>
    TState State { get; set; }

    /// <summary>Whether a backing record existed when the state was last read.</summary>
    bool RecordExists { get; }

    /// <summary>Loads the latest persisted state into <see cref="State" />.</summary>
    Task ReadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the current value of <see cref="State" />.</summary>
    Task WriteStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears any persisted state and resets <see cref="State" /> to a new instance.</summary>
    Task ClearStateAsync(CancellationToken cancellationToken = default);
}
