namespace Adventure.GrainInterfaces;

public class PlayerInfo
{
    public Guid Key { get; set; }
    public string? Name { get; set; }
}

public class Thing
{
    public string? Name { get; set; }
    public string? Category { get; set; }
    public bool CanCarry { get; set; }
}

public class MonsterInfo
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? KillOn { get; set; }
    public string? AttackMessage { get; set; }
}

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
