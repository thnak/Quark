using System.Collections.Concurrent;

namespace Quark.Transactions;

public sealed class TransactionCoordinator : ITransactionCoordinator
{
    private static readonly AsyncLocal<Guid> _currentTransactionId = new();

    private readonly ConcurrentDictionary<Guid, TransactionContext> _transactions = new();

    public Guid BeginTransaction()
    {
        var id = Guid.NewGuid();
        _transactions[id] = new TransactionContext();
        _currentTransactionId.Value = id;
        return id;
    }

    public async Task CommitAsync(Guid transactionId)
    {
        if (!_transactions.TryRemove(transactionId, out var ctx)) return;
        foreach (var (commit, _) in ctx.Writers)
            await commit().ConfigureAwait(false);
        _currentTransactionId.Value = Guid.Empty;
    }

    public Task AbortAsync(Guid transactionId, Exception? reason = null)
    {
        if (_transactions.TryRemove(transactionId, out var ctx))
            foreach (var (_, rollback) in ctx.Writers)
                rollback();
        _currentTransactionId.Value = Guid.Empty;
        return Task.CompletedTask;
    }

    public bool IsInTransaction(out Guid transactionId)
    {
        transactionId = _currentTransactionId.Value;
        return transactionId != Guid.Empty;
    }

    internal void RegisterWriter(Guid transactionId, Func<Task> commit, Action rollback)
    {
        if (_transactions.TryGetValue(transactionId, out var ctx))
            ctx.Writers.Add((commit, rollback));
    }

    private sealed class TransactionContext
    {
        public List<(Func<Task> Commit, Action Rollback)> Writers { get; } = [];
    }
}
