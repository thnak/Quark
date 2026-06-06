namespace Quark.Transactions;

/// <summary>
///     Marks a grain constructor parameter as a transactional state slot.
///     Drop-in equivalent of Orleans' <c>[TransactionalState]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class TransactionalStateAttribute : Attribute
{
    public TransactionalStateAttribute(string stateName, string? storageName = null)
    {
        StateName = stateName;
        StorageName = storageName;
    }

    public string StateName { get; }
    public string? StorageName { get; }
}
