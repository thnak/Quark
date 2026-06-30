using Quark.Serialization.Abstractions.Attributes;

namespace Realm.Common.Dtos;

[GenerateSerializer]
[Alias("EnterResult")]
public sealed class EnterResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public Coord At { get; set; } = new();
}
