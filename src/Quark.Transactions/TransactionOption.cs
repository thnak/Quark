namespace Quark.Transactions;

/// <summary>
///     Controls how a grain method participates in a transaction.
///     Drop-in equivalent of Orleans' <c>TransactionOption</c>.
/// </summary>
public enum TransactionOption
{
    Create,
    Join,
    CreateOrJoin,
    Supported,
    NotAllowed
}
