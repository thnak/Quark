using System.Numerics;
using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.AstroSim;

public interface IChunkGrain : IGrainWithStringKey
{
    ValueTask TickAsync();
    ValueTask<ChunkAggregate> GetAggregateAsync();
    ValueTask TransferBodyAsync(Body body);
    ValueTask SeedAsync(IReadOnlyList<Body> bodies);
}

public struct Body
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Mass;
}

public readonly record struct ChunkAggregate(Vector3 CenterOfMass, float TotalMass, int BodyCount);

public sealed class AstroSimOptions
{
    public required int GridSize { get; init; }
    public required float CellSize { get; init; }
    public required float Dt { get; init; }
}
