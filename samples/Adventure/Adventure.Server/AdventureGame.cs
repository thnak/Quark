using System.Text.Json;
using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Hosting;

namespace Adventure.Server;

internal static class AdventureGame
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task LoadAsync(IGrainFactory grainFactory, string mapFile)
    {
        var json = await File.ReadAllTextAsync(mapFile);
        var map = JsonSerializer.Deserialize<MapData>(json, JsonOptions)
                  ?? throw new InvalidOperationException("Failed to deserialize map.");

        foreach (var room in map.Rooms)
        {
            var roomGrain = grainFactory.GetGrain<IRoomGrain>(room.Id);
            await roomGrain.SetInfoAsync(new RoomInfo
            {
                Id          = room.Id,
                Name        = room.Name,
                Description = room.Description,
                North       = room.Exits?.GetValueOrDefault("north") ?? 0,
                South       = room.Exits?.GetValueOrDefault("south") ?? 0,
                East        = room.Exits?.GetValueOrDefault("east")  ?? 0,
                West        = room.Exits?.GetValueOrDefault("west")  ?? 0,
            });

            if (room.Things is not null)
                foreach (var t in room.Things)
                    await roomGrain.AddThingAsync(t.Name, t.Category, t.CanCarry);
        }

        if (map.Monsters is not null)
        {
            foreach (var m in map.Monsters)
            {
                var monsterGrain = grainFactory.GetGrain<IMonsterGrain>(m.Id);
                await monsterGrain.SetInfoAsync(
                    new MonsterInfo
                    {
                        Id            = m.Id,
                        Name          = m.Name,
                        KillOn        = m.KillOn,
                        AttackMessage = m.Attack,
                    },
                    m.RoomId);
            }
        }

        Console.WriteLine($"Loaded {map.Rooms.Count} rooms and {map.Monsters?.Count ?? 0} monsters.");
    }

    // ── JSON deserialization POCOs (not grain types) ──────────────────────────

    private sealed class MapData
    {
        public List<RoomData> Rooms { get; set; } = [];
        public List<MonsterData>? Monsters { get; set; }
    }

    private sealed class RoomData
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, long>? Exits { get; set; }
        public List<ThingData>? Things { get; set; }
    }

    private sealed class ThingData
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public bool CanCarry { get; set; }
    }

    private sealed class MonsterData
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? KillOn { get; set; }
        public string? Attack { get; set; }
        public long RoomId { get; set; }
    }
}
