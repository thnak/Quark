using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("MapDescriptor")]
public sealed class MapDescriptor
{
    [Id(0)] public string Id { get; set; } = "";
    [Id(1)] public string Name { get; set; } = "";
    [Id(2)] public int Width { get; set; }
    [Id(3)] public int Height { get; set; }
    [Id(4)] public string? NeighborNorth { get; set; }
    [Id(5)] public string? NeighborSouth { get; set; }
    [Id(6)] public string? NeighborEast { get; set; }
    [Id(7)] public string? NeighborWest { get; set; }
}
