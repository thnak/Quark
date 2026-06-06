namespace Quark.Transactions;

/// <summary>
///     Provides transactional read/write access to persistent grain state.
///     Drop-in equivalent of Orleans' <c>ITransactionalState&lt;TState&gt;</c>.
/// </summary>
public interface ITransactionalState<TState> where TState : new()
{
    Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction);
    Task PerformUpdate(Action<TState> updateFunction);
    Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction);
}
