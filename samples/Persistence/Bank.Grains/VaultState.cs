namespace Bank.Grains;

/// <summary>
///     Principal held in the vault. Lives in plain <c>IActivationMemory&lt;T&gt;</c> — it survives
///     across calls to the same activation but is not persisted to storage (kept simple so the
///     sample stays focused on the eager-resource pattern).
/// </summary>
public sealed class VaultState
{
    public decimal Principal { get; set; }
}

/// <summary>
///     The eager resource for <see cref="VaultBehavior" />: a snapshot of the interest rate taken
///     once, from DI, at activation time. Never persisted; rebuilt on every activation.
/// </summary>
public sealed class RateSnapshot
{
    public decimal DailyRate { get; init; }
    public DateTimeOffset PinnedAt { get; init; }
}
