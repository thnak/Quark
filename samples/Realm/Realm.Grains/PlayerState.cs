using Quark.Serialization.Abstractions.Attributes;
using Realm.Common.Dtos;

namespace Realm.Grains;

[GenerateSerializer]
[Alias("PlayerState")]
public sealed class PlayerState
{
    [Id(0)] public string MapId { get; set; } = "";
    [Id(1)] public Coord At { get; set; } = new();
    [Id(2)] public int Level { get; set; } = 1;
    [Id(3)] public int Hp { get; set; } = 100;
}
