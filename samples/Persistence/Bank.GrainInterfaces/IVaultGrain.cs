using Quark.Core.Abstractions.Grains;

namespace Bank.GrainInterfaces;

/// <summary>
///     An interest-bearing vault that demonstrates <c>IEagerActivationMemory&lt;RateSnapshot&gt;</c>.
///     At activation the grain eagerly resolves the daily interest rate from a DI service and pins
///     it for the lifetime of the activation, then applies it synchronously.
/// </summary>
public interface IVaultGrain : IGrainWithStringKey
{
    /// <summary>Adds principal to the vault. Returns the new principal.</summary>
    Task<decimal> DepositAsync(decimal amount);

    /// <summary>Applies the pinned daily rate over <paramref name="days" /> days. Returns the new principal.</summary>
    Task<decimal> AccrueAsync(int days);

    /// <summary>Returns the current principal.</summary>
    Task<decimal> GetPrincipalAsync();

    /// <summary>Describes the rate that was pinned (from DI) when this activation started.</summary>
    Task<string> GetPinnedRateAsync();
}
