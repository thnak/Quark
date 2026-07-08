using System.Numerics;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Performance.AstroSim;

public sealed class ChunkState
{
    public readonly object Gate = new();
    public readonly List<Body> Bodies = new();
    public int X;
    public int Y;
    public int Z;
    public bool CoordsInitialized;
}

[Reentrant]
public sealed class ChunkGrainBehavior : IGrainBehavior, IChunkGrain, IActivationLifecycle
{
    private const float G = 1f;
    private const float Softening = 0.5f;

    private readonly IActivationMemory<ChunkState> _memory;
    private readonly IGrainFactory _factory;
    private readonly ICallContext _ctx;
    private readonly AstroSimOptions _options;

    public ChunkGrainBehavior(
        IActivationMemory<ChunkState> memory,
        IGrainFactory factory,
        ICallContext ctx,
        AstroSimOptions options)
    {
        _memory = memory;
        _factory = factory;
        _ctx = ctx;
        _options = options;
    }

    private ChunkState S => _memory.Value;

    public Task OnActivateAsync(CancellationToken ct)
    {
        if (!S.CoordsInitialized)
        {
            string[] parts = _ctx.GrainId.Key.Split(',');
            S.X = int.Parse(parts[0]);
            S.Y = int.Parse(parts[1]);
            S.Z = int.Parse(parts[2]);
            S.CoordsInitialized = true;
        }

        return Task.CompletedTask;
    }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct) => Task.CompletedTask;

    public ValueTask SeedAsync(IReadOnlyList<Body> bodies)
    {
        lock (S.Gate)
        {
            S.Bodies.AddRange(bodies);
        }

        return default;
    }

    public ValueTask<ChunkAggregate> GetAggregateAsync()
    {
        Vector3 weightedSum = Vector3.Zero;
        float totalMass = 0f;
        int count;

        lock (S.Gate)
        {
            List<Body> bodies = S.Bodies;
            count = bodies.Count;
            foreach (Body b in bodies)
            {
                weightedSum += b.Position * b.Mass;
                totalMass += b.Mass;
            }
        }

        if (count == 0)
            return new ValueTask<ChunkAggregate>(new ChunkAggregate(Vector3.Zero, 0f, 0));

        return new ValueTask<ChunkAggregate>(new ChunkAggregate(weightedSum / totalMass, totalMass, count));
    }

    public ValueTask TransferBodyAsync(Body body)
    {
        lock (S.Gate)
        {
            S.Bodies.Add(body);
        }

        return default;
    }

    public async ValueTask TickAsync()
    {
        // This grain is [Reentrant]: the await below (neighbor.GetAggregateAsync) can interleave
        // with concurrent calls into THIS activation. Never mutate S.Bodies across an await —
        // snapshot to a private array first, do all the awaiting against that copy, and commit the
        // result back under a brief, non-awaiting lock.
        Body[] snapshot;
        lock (S.Gate)
        {
            snapshot = S.Bodies.Count == 0 ? Array.Empty<Body>() : S.Bodies.ToArray();
        }

        int n = snapshot.Length;
        if (n == 0)
            return;

        var forces = new Vector3[n];

        // Local pairwise gravity: O(k^2), k = bodies in this chunk.
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                Vector3 delta = snapshot[j].Position - snapshot[i].Position;
                float distSq = delta.LengthSquared() + Softening;
                float invDist3 = 1f / (MathF.Sqrt(distSq) * distSq);
                Vector3 unit = delta * invDist3;
                forces[i] += unit * (G * snapshot[j].Mass);
                forces[j] -= unit * (G * snapshot[i].Mass);
            }
        }

        // Neighbor aggregate pulls — the dominant message source (up to 26 grain calls/tick).
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                        continue;

                    int nx = S.X + dx, ny = S.Y + dy, nz = S.Z + dz;
                    if (nx < 0 || nx >= _options.GridSize ||
                        ny < 0 || ny >= _options.GridSize ||
                        nz < 0 || nz >= _options.GridSize)
                        continue;

                    IChunkGrain neighbor = _factory.GetGrain<IChunkGrain>($"{nx},{ny},{nz}");
                    ChunkAggregate agg = await neighbor.GetAggregateAsync();
                    if (agg.BodyCount == 0)
                        continue;

                    for (int i = 0; i < n; i++)
                    {
                        Vector3 delta = agg.CenterOfMass - snapshot[i].Position;
                        float distSq = delta.LengthSquared() + Softening;
                        float invDist3 = 1f / (MathF.Sqrt(distSq) * distSq);
                        forces[i] += delta * invDist3 * (G * agg.TotalMass);
                    }
                }
            }
        }

        // Integrate and clamp against the private snapshot — no lock needed here, nothing else can
        // see it.
        float worldMax = _options.GridSize * _options.CellSize;
        var retained = new List<Body>(n);
        List<(int x, int y, int z, Body body)>? transfers = null;

        for (int i = 0; i < n; i++)
        {
            Body b = snapshot[i];
            b.Velocity += forces[i] / MathF.Max(b.Mass, 0.0001f) * _options.Dt;
            b.Position += b.Velocity * _options.Dt;

            ClampAxis(ref b.Position.X, ref b.Velocity.X, worldMax);
            ClampAxis(ref b.Position.Y, ref b.Velocity.Y, worldMax);
            ClampAxis(ref b.Position.Z, ref b.Velocity.Z, worldMax);

            int destX = Math.Clamp((int)(b.Position.X / _options.CellSize), 0, _options.GridSize - 1);
            int destY = Math.Clamp((int)(b.Position.Y / _options.CellSize), 0, _options.GridSize - 1);
            int destZ = Math.Clamp((int)(b.Position.Z / _options.CellSize), 0, _options.GridSize - 1);

            if (destX != S.X || destY != S.Y || destZ != S.Z)
                (transfers ??= new List<(int, int, int, Body)>()).Add((destX, destY, destZ, b));
            else
                retained.Add(b);
        }

        // Commit: replace the n bodies we snapshotted with their integrated results. Bodies appended
        // concurrently by TransferBodyAsync/SeedAsync while we awaited neighbors are untouched — this
        // grain is reentrant, but only one TickAsync runs per chunk at a time (the driver awaits a
        // full tick before starting the next), and Transfer/Seed only ever append, so the first n
        // entries in the live list are still exactly the ones we snapshotted.
        lock (S.Gate)
        {
            S.Bodies.RemoveRange(0, n);
            S.Bodies.InsertRange(0, retained);
        }

        if (transfers is null)
            return;

        var transferTasks = new Task[transfers.Count];
        for (int i = 0; i < transfers.Count; i++)
        {
            (int x, int y, int z, Body body) = transfers[i];
            IChunkGrain dest = _factory.GetGrain<IChunkGrain>($"{x},{y},{z}");
            transferTasks[i] = dest.TransferBodyAsync(body).AsTask();
        }

        await Task.WhenAll(transferTasks);
    }

    private static void ClampAxis(ref float pos, ref float vel, float max)
    {
        if (pos < 0f)
        {
            pos = 0f;
            vel = -vel;
        }
        else if (pos >= max)
        {
            pos = max - 0.001f;
            vel = -vel;
        }
    }
}
