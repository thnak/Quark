using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Quark.Transactions;

public static class TransactionServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Quark transaction coordinator.
    ///     Drop-in equivalent of Orleans' <c>UseTransactions()</c>.
    /// </summary>
    public static IServiceCollection AddTransactions(this IServiceCollection services)
    {
        services.TryAddSingleton<TransactionCoordinator>();
        services.TryAddSingleton<ITransactionCoordinator>(sp =>
            sp.GetRequiredService<TransactionCoordinator>());
        return services;
    }
}
