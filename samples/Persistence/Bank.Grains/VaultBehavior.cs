using Bank.GrainInterfaces;
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Bank.Grains;

/// <summary>
///     Pattern 4 — <b>Eager activation memory</b> (<see cref="IEagerActivationMemory{T}" />).
///     <para>
///         The eager factory is configured in the constructor with <c>Load</c> and runs
///         <b>eagerly, before <c>OnActivateAsync</c></b>, inside the activation scope — so it can
///         resolve DI services (here <see cref="IInterestRateService" />). Once activation
///         completes, <c>Value</c> is available <b>synchronously</b> on every call, no
///         <c>await</c> needed. This fills the gap between plain activation memory (no DI, sync) and
///         managed memory (lazy async, but no DI in the factory).
///     </para>
///     <para>
///         The rate is captured once per activation, so all calls on this activation see a stable
///         "pinned" rate even if the underlying service later changes. <c>Destroy</c> would run
///         after <c>OnDeactivateAsync</c>; this resource needs no cleanup.
///     </para>
/// </summary>
public sealed class VaultBehavior : IGrainBehavior, IVaultGrain, IActivationLifecycle
{
    private readonly IEagerActivationMemory<RateSnapshot> _rate;
    private readonly IActivationMemory<VaultState> _memory;

    public VaultBehavior(
        IEagerActivationMemory<RateSnapshot> rate,
        IActivationMemory<VaultState> memory)
    {
        _memory = memory;
        _rate = rate.Load((sp, _) =>
        {
            // DI access inside the factory — the distinguishing feature of eager memory.
            IInterestRateService rates = sp.GetRequiredService<IInterestRateService>();
            return ValueTask.FromResult(new RateSnapshot
            {
                DailyRate = rates.CurrentDailyRate,
                PinnedAt = DateTimeOffset.UtcNow,
            });
        });
    }

    // The eager Value is guaranteed initialized here — init ran before OnActivateAsync.
    public Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public Task<decimal> DepositAsync(decimal amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Deposit must be positive.");
        _memory.Value.Principal += amount;
        return Task.FromResult(_memory.Value.Principal);
    }

    public Task<decimal> AccrueAsync(int days)
    {
        if (days <= 0) throw new ArgumentOutOfRangeException(nameof(days), "Days must be positive.");
        // Synchronous access to the eagerly-loaded, DI-sourced rate — no await.
        decimal interest = _memory.Value.Principal * _rate.Value.DailyRate * days;
        _memory.Value.Principal += interest;
        return Task.FromResult(_memory.Value.Principal);
    }

    public Task<decimal> GetPrincipalAsync() => Task.FromResult(_memory.Value.Principal);

    public Task<string> GetPinnedRateAsync() =>
        Task.FromResult($"{_rate.Value.DailyRate:P4} per day (pinned at activation {_rate.Value.PinnedAt:u})");
}
