namespace Quark.Transactions;

/// <summary>
///     Manages transaction lifecycle: begin, commit, abort, 2PC coordination.
/// </summary>
public interface ITransactionCoordinator
{
    Guid BeginTransaction();// TODO did not implemented or used in any elsewhere
    Task CommitAsync(Guid transactionId);// TODO did not implemented or used in any elsewhere
    Task AbortAsync(Guid transactionId, Exception? reason = null);// TODO did not implemented or used in any elsewhere
    bool IsInTransaction(out Guid transactionId);// TODO did not implemented or used in any elsewhere
}
