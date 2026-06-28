using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization.Abstractions.Abstractions;

namespace Bank.Grains;

/// <summary>
///     Registers the deep copiers that the storage providers use to snapshot durable state.
///     <para>
///         The code generator emits an internal <c>{StateType}Copier</c> for every
///         <c>[GenerateSerializer]</c> type. They are <c>internal</c> to the assembly that declares
///         the state, so this helper — living in the same assembly — wires them into DI. State that
///         is never written through <c>IGrainStorage</c> (e.g. the event-sourced
///         <see cref="LedgerState" />) does not need a copier.
///     </para>
/// </summary>
public static class BankStateCopiers
{
    /// <summary>Registers <c>IDeepCopier&lt;T&gt;</c> for every storage-backed Bank state type.</summary>
    public static IServiceCollection AddBankStateCopiers(this IServiceCollection services)
    {
        services.AddSingleton<IDeepCopier<AccountState>>(
            sp => new AccountStateCopier(sp.GetRequiredService<ICopierProvider>()));
        services.AddSingleton<IDeepCopier<ProfileState>>(
            sp => new ProfileStateCopier(sp.GetRequiredService<ICopierProvider>()));
        return services;
    }
}
