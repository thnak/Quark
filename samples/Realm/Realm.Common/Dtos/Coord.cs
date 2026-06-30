using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("Coord")]
public sealed class Coord
{
    [Id(0)] public int X { get; set; }
    [Id(1)] public int Y { get; set; }
}
