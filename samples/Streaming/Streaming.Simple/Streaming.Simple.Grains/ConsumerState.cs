using Quark.Streaming.Abstractions;

namespace Streaming.Simple.Grains;

public sealed class ConsumerState
{
    public StreamSubscriptionHandle<int>? Handle { get; set; }
}