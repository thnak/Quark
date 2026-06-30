using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("MoveResult")]
public sealed class MoveResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public Coord? NewCoord { get; set; }
    [Id(2)] public string? TransitionMapId { get; set; }
    [Id(3)] public Coord? TransitionCoord { get; set; }
}
