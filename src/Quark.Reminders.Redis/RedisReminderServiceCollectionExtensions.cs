using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Reminders;
using Quark.Reminders.Abstractions;
using StackExchange.Redis;

namespace Quark.Reminders.Redis;

/// <summary>Service registration helpers for the Redis reminder provider.</summary>
public static class RedisReminderServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Redis-backed reminder storage and polling service.
    ///     Call after <c>AddQuarkRuntime()</c>.
    /// </summary>
    public static IServiceCollection AddRedisReminders(
        this IServiceCollection services,
        Action<RedisReminderOptions>? configure = null)
    {
        services.AddOptions<RedisReminderOptions>();

        if (configure is not null)
            services.Configure(configure);

        // Bridge Redis poll interval → ReminderOptions so DefaultReminderService sees it.
        services.TryAddSingleton<IOptions<ReminderOptions>>(sp =>
        {
            RedisReminderOptions redisOpts = sp.GetRequiredService<IOptions<RedisReminderOptions>>().Value;
            return Options.Create(new ReminderOptions { PollInterval = redisOpts.PollInterval });
        });

        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            string connectionString = sp.GetRequiredService<IOptions<RedisReminderOptions>>().Value.ConnectionString;
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(RedisReminderOptions.ConnectionString));
            return ConnectionMultiplexer.Connect(connectionString);
        });

        services.TryAddSingleton<IReminderStorage, RedisReminderStorage>();
        services.TryAddSingleton<DefaultReminderService>();
        services.TryAddSingleton<IReminderService>(sp => sp.GetRequiredService<DefaultReminderService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DefaultReminderService>(
            sp => sp.GetRequiredService<DefaultReminderService>()));

        return services;
    }
}
