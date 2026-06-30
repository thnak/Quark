namespace Realm.Common.Models;

public sealed class MapContent
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public TileGrid Grid { get; set; } = new();
    public SpawnPoint[] SpawnPoints { get; set; } = [];
    public MapNeighbors Neighbors { get; set; } = new();
}
