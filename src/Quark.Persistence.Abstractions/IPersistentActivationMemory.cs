namespace Quark.Persistence.Abstractions;

/// <summary>
///     Combines <see cref="Quark.Core.Abstractions.Hosting.IActivationMemory{TState}" /> with
///     <see cref="IGrainStorage" /> read/write operations.
///     The Value lives in the GrainActivation shell across all calls to the same activation.
/// </summary>
/// <remarks>
///     Typical usage:
///     <list type="bullet">
///       <item>Call <see cref="LoadAsync" /> in <c>IActivationLifecycle.OnActivateAsync</c>.</item>
///       <item>Read <see cref="Value" /> freely on any call (no storage round-trip).</item>
///       <item>Call <see cref="SaveAsync" /> after mutations that must survive deactivation.</item>
///     </list>
/// </remarks>
public interface IPersistentActivationMemory<TState>
    where TState : class, new()
{
    /// <summary>Current in-memory state. Initially a default-constructed instance.</summary>
    TState Value { get; }

    /// <summary>Loads state from <see cref="IGrainStorage" /> into Value. Call once in OnActivateAsync.</summary>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>Persists Value to <see cref="IGrainStorage" />.</summary>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>Clears persisted state and resets Value to a new default instance.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
