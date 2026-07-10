using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quark.Core.Hosting;
using Quark.Persistence.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Silo builder extensions that enable idempotency-key dedup support.
/// </summary>
public static class IdempotencySiloBuilderExtensions
{
    /// <summary>
    ///     Registers the <see cref="IRequestDedupStore" /> and enables the dedup checkpoint in
    ///     <c>MessageDispatcher</c>. Opt-in; silos that do not call this method pay no overhead.
    ///     <see cref="IdempotencyOptions.Durability" /> selects the implementation: the default
    ///     <see cref="DedupDurability.InMemory" /> tier needs nothing further; the
    ///     <see cref="DedupDurability.Durable" /> tier reads its store from the named
    ///     <c>IGrainStorage</c> provider given by <see cref="IdempotencyOptions.DurableProviderName" />
    ///     (register it first, e.g. via <c>AddInMemoryGrainStorage(name)</c>/<c>AddRedisGrainStorage(name)</c>).
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

        // Durable-tier wire support — hand-written since Quark.Runtime does not run the source
        // generator (see DurableDedupRecordCodec). Harmless to register even for the in-memory tier.
        builder.Services.TryAddSingleton<IFieldCodec<DurableDedupRecord>, DurableDedupRecordCodec>();
        builder.Services.TryAddSingleton<IDeepCopier<DurableDedupRecord>, DurableDedupRecordCopier>();

        builder.Services.AddSingleton<IRequestDedupStore>(sp =>
        {
            IdempotencyOptions opts = sp.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
            return opts.Durability == DedupDurability.Durable
                ? new DurableRequestDedupStore(
                    sp.GetRequiredKeyedService<IGrainStorage>(opts.DurableProviderName),
                    sp.GetRequiredService<IOptions<IdempotencyOptions>>())
                : new InMemoryRequestDedupStore(sp.GetRequiredService<IOptions<IdempotencyOptions>>());
        });

        return builder;
    }
}
