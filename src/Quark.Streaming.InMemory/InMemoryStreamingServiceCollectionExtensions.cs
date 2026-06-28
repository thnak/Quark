using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public static class InMemoryStreamingServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a named in-memory stream provider.
    ///     Drop-in equivalent of Orleans' <c>AddMemoryStreams(name)</c>.
    /// </summary>
    public static IServiceCollection AddMemoryStreams(this IServiceCollection services, string providerName)
    {
        services.TryAddSingleton<StreamSubscriptionRegistry>(sp => new StreamSubscriptionRegistry(
            sp.GetService<ImplicitStreamSubscriptionRegistry>(),
            sp.GetService<IImplicitStreamActivator>()));
        services.TryAddSingleton<IUntypedStreamSubscriptionRegistry>(
            sp => sp.GetRequiredService<StreamSubscriptionRegistry>());
        services.AddKeyedSingleton<IStreamProvider>(providerName,
            (sp, _) => new InMemoryStreamProvider(
                providerName,
                sp.GetRequiredService<StreamSubscriptionRegistry>()));
        return services;
    }

    /// <summary>
    ///     Declares that publishing to streams under <paramref name="streamNamespace" /> should
    ///     auto-activate the grain type identified by <paramref name="grainTypeKey" /> whose key
    ///     matches the stream key. The grain's <c>OnActivateAsync</c> is responsible for
    ///     subscribing itself; auto-activation ensures the first published item is not lost.
    ///     Call <c>AddQuarkRuntime()</c> on the silo side to supply the activation back-end.
    /// </summary>
    public static IServiceCollection AddImplicitStreamSubscription(
        this IServiceCollection services,
        string streamNamespace,
        string grainTypeKey)
    {
        services.TryAddSingleton<ImplicitStreamSubscriptionRegistry>(sp =>
        {
            var registry = new ImplicitStreamSubscriptionRegistry();
            foreach (IImplicitStreamSubscriptionRegistration reg in sp.GetServices<IImplicitStreamSubscriptionRegistration>())
                registry.Register(reg.StreamNamespace, reg.GrainTypeKey);
            return registry;
        });

        services.AddSingleton<IImplicitStreamSubscriptionRegistration>(
            new ImplicitStreamSubscriptionEntry(streamNamespace, grainTypeKey));
        return services;
    }

    // -----------------------------------------------------------------------

    internal interface IImplicitStreamSubscriptionRegistration
    {
        string StreamNamespace { get; }
        string GrainTypeKey { get; }
    }

    private sealed class ImplicitStreamSubscriptionEntry(string streamNamespace, string grainTypeKey)
        : IImplicitStreamSubscriptionRegistration
    {
        public string StreamNamespace => streamNamespace;
        public string GrainTypeKey => grainTypeKey;
    }
}
