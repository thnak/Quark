namespace Adventure.GrainInterfaces;

public class RoomInfo
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long North { get; set; }
    public long South { get; set; }
    public long East { get; set; }
    public long West { get; set; }
}