using Quark.Serialization.Abstractions.Attributes;

namespace Bank.Grains;

/// <summary>
///     Durable state for <see cref="AccountBehavior" />. <c>[GenerateSerializer]</c> makes the
///     code generator emit an <c>IDeepCopier&lt;AccountState&gt;</c> the storage provider uses to
///     snapshot the value on read and write.
/// </summary>
[GenerateSerializer]
public sealed class AccountState
{
    [Id(0)] public decimal Balance { get; set; }
    [Id(1)] public int TransactionCount { get; set; }
}
