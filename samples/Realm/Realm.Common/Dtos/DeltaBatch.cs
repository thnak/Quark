using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("DeltaBatch")]
public sealed class DeltaBatch
{
    [Id(0)] public string MapId { get; set; } = "";
    [Id(1)] public long TickUtc { get; set; }
    [Id(2)] public EntityDelta[] Deltas { get; set; } = [];
}
