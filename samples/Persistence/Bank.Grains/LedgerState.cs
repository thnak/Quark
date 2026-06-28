namespace Bank.Grains;

/// <summary>
///     In-memory projection for <see cref="LedgerBehavior" />. This is never persisted directly —
///     it is rebuilt by replaying <see cref="LedgerEvent" />s from the log. No serializer is needed
///     because the projection lives only in the activation shell.
/// </summary>
public sealed class LedgerState
{
    public decimal Balance { get; set; }
    public List<string> History { get; } = [];
}

/// <summary>Base type for ledger events. Events are the source of truth, persisted to the log.</summary>
public abstract record LedgerEvent;

/// <summary>Money paid into the ledger.</summary>
public sealed record Credited(decimal Amount, string Note) : LedgerEvent;

/// <summary>Money paid out of the ledger.</summary>
public sealed record Debited(decimal Amount, string Note) : LedgerEvent;
