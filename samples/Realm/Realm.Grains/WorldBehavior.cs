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
        MapContent? map = null;
        SpawnPoint? spawn = null;
        foreach (MapContent mc in _content.All.Values)
        {
            if (mc.SpawnPoints.Length > 0)
            {
                map = mc;
                spawn = mc.SpawnPoints[0];
                break;
            }
        }

        if (map is null || spawn is null)
            throw new InvalidOperationException("No maps with spawn points are defined in content.");

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
