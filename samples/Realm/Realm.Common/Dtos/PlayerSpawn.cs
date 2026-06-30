using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("PlayerSpawn")]
public sealed class PlayerSpawn
{
    [Id(0)] public string MapId { get; set; } = "";
    [Id(1)] public Coord At { get; set; } = new();
}
