namespace Quark.Transactions;

/// <summary>
///     Marks a grain method as participating in a transaction.
///     Drop-in equivalent of Orleans' <c>[Transaction]</c>.
///     In Phase 5 this is metadata only; auto-coordination middleware is deferred.
///     Tests coordinate manually via <see cref="ITransactionCoordinator"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TransactionAttribute : Attribute
{
    public TransactionAttribute(TransactionOption option = TransactionOption.CreateOrJoin)
        => Option = option;

    public TransactionOption Option { get; }
}
