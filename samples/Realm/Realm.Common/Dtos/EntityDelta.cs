using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("EntityDelta")]
public sealed class EntityDelta
{
    [Id(0)] public string EntityId { get; set; } = "";
    [Id(1)] public EntityKind Kind { get; set; }
    [Id(2)] public Coord? At { get; set; }
    [Id(3)] public bool Removed { get; set; }
}
