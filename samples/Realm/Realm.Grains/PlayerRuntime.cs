using Quark.Streaming.Abstractions;
using Realm.Common.Dtos;

namespace Realm.Grains;

public sealed class PlayerRuntime
{
    public Dictionary<string, StreamSubscriptionHandle<DeltaBatch>> Subscriptions { get; } = new(StringComparer.Ordinal);
    public int ReceivedDeltaCount { get; set; }
}
