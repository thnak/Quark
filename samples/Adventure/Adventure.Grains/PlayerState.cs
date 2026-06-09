using Adventure.GrainInterfaces;

namespace Adventure.Grains;

public sealed class PlayerState
{
    public string? Name { get; set; }
    public IRoomGrain? Room { get; set; }
    public List<Thing> Inventory { get; } = [];
}