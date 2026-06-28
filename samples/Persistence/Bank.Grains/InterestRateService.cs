namespace Bank.Grains;

/// <summary>
///     A plain DI singleton. The point of the eager-memory sample is that
///     <see cref="VaultBehavior" /> resolves this from the activation scope inside its eager
///     factory — something <c>IManagedActivationMemory&lt;T&gt;</c> cannot do (its factory takes no
///     <see cref="IServiceProvider" />).
/// </summary>
public interface IInterestRateService
{
    /// <summary>The daily interest rate applied to vault balances (e.g. 0.0005 = 0.05%/day).</summary>
    decimal CurrentDailyRate { get; }
}

/// <summary>Default implementation with a fixed rate. Swap freely — it is resolved through DI.</summary>
public sealed class InterestRateService : IInterestRateService
{
    public decimal CurrentDailyRate => 0.0005m;
}
