using Quark.Serialization.Abstractions.Attributes;

namespace Bank.Grains;

/// <summary>
///     Projection for <see cref="LedgerBehavior" />, rebuilt by replaying <see cref="LedgerEvent" />s.
///     <c>[GenerateSerializer]</c> lets the in-memory <c>ISnapshotStore</c> deep-copy it so activation
///     can replay only post-snapshot events instead of the whole log.
/// </summary>
[GenerateSerializer]
public sealed class LedgerState
{
    [Id(0)] public decimal Balance { get; set; }
    [Id(1)] public List<string> History { get; set; } = [];
}

/// <summary>Base type for ledger events. Events are the source of truth, persisted to the log.</summary>
public abstract record LedgerEvent;

/// <summary>Money paid into the ledger.</summary>
public sealed record Credited(decimal Amount, string Note) : LedgerEvent;

/// <summary>Money paid out of the ledger.</summary>
public sealed record Debited(decimal Amount, string Note) : LedgerEvent;
