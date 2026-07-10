using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;

namespace Bank.Grains;

/// <summary>
///     Registers the deep copiers that storage providers and the <c>ISnapshotStore</c> use to
///     snapshot durable/journaled state.
///     <para>
///         The code generator emits an internal <c>{StateType}Copier</c> for every
///         <c>[GenerateSerializer]</c> type. They are <c>internal</c> to the assembly that declares
///         the state, so this helper — living in the same assembly — wires them into DI. This
///         includes <see cref="LedgerState" />, whose copier lets the in-memory
///         <c>ISnapshotStore</c> snapshot the event-sourced ledger projection.
///     </para>
/// </summary>
public static class BankStateCopiers
{
    /// <summary>Registers <c>IDeepCopier&lt;T&gt;</c> for every storage-backed or snapshotted Bank state type.</summary>
    public static IServiceCollection AddBankStateCopiers(this IServiceCollection services)
    {
        services.AddSingleton<IDeepCopier<AccountState>>(
            sp => new AccountStateCopier(sp.GetRequiredService<ICopierProvider>()));
        services.AddSingleton<IDeepCopier<ProfileState>>(
            sp => new ProfileStateCopier(sp.GetRequiredService<ICopierProvider>()));
        services.AddSingleton<IDeepCopier<LedgerState>>(
            sp => new LedgerStateCopier(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }
}
