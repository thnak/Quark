namespace Quark.Transactions;

/// <summary>
///     Manages transaction lifecycle: begin, commit, abort, 2PC coordination.
/// </summary>
public interface ITransactionCoordinator
{
    Guid BeginTransaction();
    Task CommitAsync(Guid transactionId);
    Task AbortAsync(Guid transactionId, Exception? reason = null);
    bool IsInTransaction(out Guid transactionId);
}
