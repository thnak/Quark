using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;

namespace Quark.Reminders.InMemory;

/// <summary>Service registration helpers for the in-memory reminder provider.</summary>
public static class InMemoryReminderServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-memory reminder storage and polling service.
    ///     Suitable for development and testing. Not durable across restarts.
    ///     Call after <c>AddQuarkRuntime()</c>.
    /// </summary>
    public static IServiceCollection AddInMemoryReminders(
        this IServiceCollection services,
        Action<ReminderOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        services.TryAddSingleton<IReminderStorage, InMemoryReminderStorage>();
        services.TryAddSingleton<DefaultReminderService>();
        services.TryAddSingleton<IReminderService>(sp => sp.GetRequiredService<DefaultReminderService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(
            sp => sp.GetRequiredService<DefaultReminderService>()));

        return services;
    }
}
