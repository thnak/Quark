using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("MapSnapshot")]
public sealed class MapSnapshot
{
    [Id(0)] public string MapId { get; set; } = "";
    [Id(1)] public EntitySnapshot[] Entities { get; set; } = [];
}
