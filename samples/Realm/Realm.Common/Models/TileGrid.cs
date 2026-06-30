namespace Realm.Common.Models;

public sealed class TileGrid
{
    // Each entry is a [row, col] pair that is blocked.
    public int[][] BlockedCoords { get; set; } = [];
}
