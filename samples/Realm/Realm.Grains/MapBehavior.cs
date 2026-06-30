using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Placement;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Realm.Common.Dtos;
using Realm.Common.Models;
using Realm.Content;
using Realm.GrainInterfaces;

namespace Realm.Grains;

[HashBasedPlacement]
public sealed class MapBehavior : IGrainBehavior, IMapGrain, IActivationLifecycle
{
    private readonly IActivationMemory<MapRuntime> _memory;
    private readonly RealmContentLoader _content;
    private readonly ICallContext _ctx;
    private readonly IActivationShellAccessor _shell;

    public MapBehavior(
        IActivationMemory<MapRuntime> memory,
        RealmContentLoader content,
        ICallContext ctx,
        IActivationShellAccessor shell)
    {
        _memory = memory;
        _content = content;
        _ctx = ctx;
        _shell = shell;
    }

    private MapRuntime S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct)
    {
        string mapId = _ctx.GrainId.Key;
        MapContent mc = _content.GetMap(mapId)
            ?? throw new InvalidOperationException($"Unknown map '{mapId}'.");

        S.Content = mc;
        foreach (int[] pair in mc.Grid.BlockedCoords)
        {
            // BlockedCoords entries are [row, col] = [Y, X]
            S.BlockedTiles.Add((pair[1], pair[0]));
        }

        S.TickTimer = _shell.Shell.RegisterTimer<MapRuntime>(
            static (state, _) =>
            {
                state.PendingDeltas.Clear();
                return Task.CompletedTask;
            },
            S,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(100),
                Period = TimeSpan.FromMilliseconds(100)
            });

        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        S.TickTimer?.Dispose();
        S.TickTimer = null;
        return Task.CompletedTask;
    }

    public Task<EnterResult> EnterAsync(string entityId, Coord at, EntityKind kind)
    {
        if (!IsInBounds(at))
            return Task.FromResult(new EnterResult { Success = false, At = at });

        S.Roster[entityId] = new EntityEntry { At = CloneCoord(at), Kind = kind };
        S.PendingDeltas.Add(new EntityDelta { EntityId = entityId, Kind = kind, At = CloneCoord(at) });
        return Task.FromResult(new EnterResult { Success = true, At = at });
    }

    public Task LeaveAsync(string entityId)
    {
        if (S.Roster.Remove(entityId))
            S.PendingDeltas.Add(new EntityDelta { EntityId = entityId, Removed = true });
        return Task.CompletedTask;
    }

    public Task<MoveResult> TryMoveAsync(string entityId, Direction dir)
    {
        if (!S.Roster.TryGetValue(entityId, out EntityEntry? entry))
            return Task.FromResult(new MoveResult { Success = false });

        MapContent content = S.Content!;
        int newX = entry.At.X + DeltaX(dir);
        int newY = entry.At.Y + DeltaY(dir);

        if (newX < 0 || newX >= content.Width || newY < 0 || newY >= content.Height)
        {
            string? neighborId = GetNeighborId(dir, content);
            if (neighborId is null)
                return Task.FromResult(new MoveResult { Success = false });

            MapContent? neighbor = _content.GetMap(neighborId);
            return Task.FromResult(new MoveResult
            {
                Success = true,
                TransitionMapId = neighborId,
                TransitionCoord = ComputeEntryCoord(dir, entry.At, neighbor)
            });
        }

        if (S.BlockedTiles.Contains((newX, newY)))
            return Task.FromResult(new MoveResult { Success = false });

        foreach ((string id, EntityEntry other) in S.Roster)
        {
            if (id != entityId && other.At.X == newX && other.At.Y == newY)
                return Task.FromResult(new MoveResult { Success = false });
        }

        Coord newCoord = new() { X = newX, Y = newY };
        entry.At = newCoord;
        S.PendingDeltas.Add(new EntityDelta { EntityId = entityId, Kind = entry.Kind, At = CloneCoord(newCoord) });
        return Task.FromResult(new MoveResult { Success = true, NewCoord = newCoord });
    }

    public Task<MapSnapshot> SnapshotAsync()
    {
        var entities = new EntitySnapshot[S.Roster.Count];
        int i = 0;
        foreach ((string id, EntityEntry e) in S.Roster)
        {
            entities[i++] = new EntitySnapshot { EntityId = id, Kind = e.Kind, At = CloneCoord(e.At) };
        }
        return Task.FromResult(new MapSnapshot { MapId = _ctx.GrainId.Key, Entities = entities });
    }

    private bool IsInBounds(Coord at) =>
        at.X >= 0 && at.X < S.Content!.Width && at.Y >= 0 && at.Y < S.Content!.Height;

    private static int DeltaX(Direction dir) => dir switch
    {
        Direction.East => 1,
        Direction.West => -1,
        _ => 0
    };

    private static int DeltaY(Direction dir) => dir switch
    {
        Direction.North => -1,
        Direction.South => 1,
        _ => 0
    };

    private static string? GetNeighborId(Direction dir, MapContent content) => dir switch
    {
        Direction.North => content.Neighbors.North,
        Direction.South => content.Neighbors.South,
        Direction.East => content.Neighbors.East,
        Direction.West => content.Neighbors.West,
        _ => null
    };

    private static Coord ComputeEntryCoord(Direction dir, Coord current, MapContent? neighbor)
    {
        int nw = neighbor?.Width ?? 20;
        int nh = neighbor?.Height ?? 20;
        return dir switch
        {
            Direction.North => new Coord { X = current.X, Y = nh - 1 },
            Direction.South => new Coord { X = current.X, Y = 0 },
            Direction.East => new Coord { X = 0, Y = current.Y },
            Direction.West => new Coord { X = nw - 1, Y = current.Y },
            _ => new Coord()
        };
    }

    private static Coord CloneCoord(Coord c) => new() { X = c.X, Y = c.Y };
}
