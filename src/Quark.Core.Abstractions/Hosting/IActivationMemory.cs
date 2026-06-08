namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Provides access to in-memory state that lives in the GrainActivation shell.
///     The TState value is created once per activation and reused across all calls.
///     NOT persisted — lost when the activation deactivates or the silo restarts.
///     Use <see cref="Quark.Persistence.Abstractions.IPersistentActivationMemory{TState}" /> for durable state.
/// </summary>
/// <remarks>
///     Thread-safety: mutations are safe only from within the grain's mailbox.
///     Do not mutate Value from timer callbacks or external threads without
///     routing through GrainActivation.PostAsync.
/// </remarks>
public interface IActivationMemory<TState>
    where TState : class, new()
{
    TState Value { get; }
}
