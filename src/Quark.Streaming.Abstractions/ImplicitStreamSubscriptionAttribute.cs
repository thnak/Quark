namespace Quark.Streaming.Abstractions;

/// <summary>
///     Marks a grain class as an implicit subscriber to any stream whose namespace matches
///     <see cref="StreamNamespace"/>. The grain should subscribe in <c>OnActivateAsync</c>
///     via <c>ServiceProvider.GetRequiredKeyedService&lt;IStreamProvider&gt;(name).GetStream&lt;T&gt;(streamId).SubscribeAsync(this)</c>.
///     Drop-in equivalent of Orleans' <c>[ImplicitStreamSubscription]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ImplicitStreamSubscriptionAttribute : Attribute
{
    public ImplicitStreamSubscriptionAttribute(string streamNamespace)
        => StreamNamespace = streamNamespace;

    public string StreamNamespace { get; }
}
