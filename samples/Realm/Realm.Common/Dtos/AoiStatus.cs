using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("AoiStatus")]
public sealed class AoiStatus
{
    [Id(0)] public string[] SubscribedMapIds { get; set; } = [];
    [Id(1)] public int ReceivedDeltaCount { get; set; }
}
