using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Placement;
using Realm.Common.Dtos;
using Realm.Common.Models;
using Realm.Content;
using Realm.GrainInterfaces;

namespace Realm.Grains;

[HashBasedPlacement]
public sealed class WorldBehavior : IGrainBehavior, IWorldGrain
{
    private readonly RealmContentLoader _content;

    public WorldBehavior(RealmContentLoader content) => _content = content;

    public Task<PlayerSpawn> LoginAsync(string playerId)
    {
        List<MapContent> eligible = _content.All.Values
            .Where(mc => mc.SpawnPoints.Length > 0)
            .OrderBy(mc => mc.Id, StringComparer.Ordinal)
            .ToList();

        if (eligible.Count == 0)
            throw new InvalidOperationException("No maps with spawn points are defined in content.");

        // Deterministic per-playerId distribution across maps (stable for the lifetime of this
        // process — GetHashCode is randomized per-process but constant within it — so repeat
        // logins land the same player on the same map; different players spread across the world).
        int idx = (int)((uint)playerId.GetHashCode() % (uint)eligible.Count);
        MapContent map = eligible[idx];
        SpawnPoint spawn = map.SpawnPoints[0];

        return Task.FromResult(new PlayerSpawn
        {
            MapId = map.Id,
            At = new Coord { X = spawn.X, Y = spawn.Y }
        });
    }

    public Task<MapDescriptor> GetMapAsync(string mapId)
    {
        MapContent mc = _content.GetMap(mapId)
            ?? throw new InvalidOperationException($"Unknown map '{mapId}'.");

        return Task.FromResult(new MapDescriptor
        {
            Id = mc.Id,
            Name = mc.Name,
            Width = mc.Width,
            Height = mc.Height,
            NeighborNorth = mc.Neighbors.North,
            NeighborSouth = mc.Neighbors.South,
            NeighborEast = mc.Neighbors.East,
            NeighborWest = mc.Neighbors.West
        });
    }
}
