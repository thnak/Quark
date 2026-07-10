using Quark.Core.Abstractions.Timers;
using Quark.Streaming.Abstractions;
using Realm.Common.Dtos;
using Realm.Common.Models;

namespace Realm.Grains;

public sealed class EntityEntry
{
    public Coord At { get; set; } = new();
    public EntityKind Kind { get; set; }
}

public sealed class MapRuntime
{
    public string MapId { get; set; } = "";
    public MapContent? Content { get; set; }
    public HashSet<(int X, int Y)> BlockedTiles { get; } = new();
    public Dictionary<string, EntityEntry> Roster { get; } = new(StringComparer.Ordinal);
    public List<EntityDelta> PendingDeltas { get; } = new();
    public IGrainTimer? TickTimer { get; set; }
    public IAsyncStream<DeltaBatch>? Stream { get; set; }
}
