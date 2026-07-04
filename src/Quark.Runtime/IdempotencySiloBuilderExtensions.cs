using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quark.Core.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Silo builder extensions that enable idempotency-key dedup support.
/// </summary>
public static class IdempotencySiloBuilderExtensions// TODO did not implemented or used in any elsewhere
{
    /// <summary>
    ///     Registers the <see cref="IRequestDedupStore" /> and enables the dedup checkpoint in
    ///     <c>MessageDispatcher</c>. Opt-in; silos that do not call this method pay no overhead.
    /// </summary>
    public static ISiloBuilder AddIdempotentCalls(
        this ISiloBuilder builder,
        Action<IdempotencyOptions>? configure = null)
    {
        if (configure is not null)
            builder.Services.Configure(configure);

        builder.Services.AddOptions<IdempotencyOptions>()
            .Validate(o => o.Window > TimeSpan.Zero, $"{nameof(IdempotencyOptions.Window)} must be positive.")
            .Validate(
                o => o.Durability != DedupDurability.Durable || o.DurableProviderName is not null,
                $"{nameof(IdempotencyOptions.DurableProviderName)} must be set when Durability is Durable.");

        builder.Services.AddSingleton<IRequestDedupStore>(sp =>
            new InMemoryRequestDedupStore(sp.GetRequiredService<IOptions<IdempotencyOptions>>()));

        return builder;
    }
}
