using System.Numerics;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Performance.AstroSim;

internal readonly struct ChunkBehavior_TickInvokable : IGrainVoidInvokable
{
    public uint MethodId => 0u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).TickAsync();
    public void Serialize(ref CodecWriter writer) { }
}

internal readonly struct ChunkBehavior_GetAggregateInvokable : IGrainInvokable<ChunkAggregate>
{
    public uint MethodId => 1u;
    public ValueTask<ChunkAggregate> Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).GetAggregateAsync();
    public void Serialize(ref CodecWriter writer) { }

    public ChunkAggregate DeserializeResult(ref CodecReader reader)
    {
        float x = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float y = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float z = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        float totalMass = BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
        int bodyCount = reader.ReadInt32();
        return new ChunkAggregate(new Vector3(x, y, z), totalMass, bodyCount);
    }
}

internal readonly struct ChunkBehavior_TransferBodyInvokable(Body body) : IGrainVoidInvokable
{
    public uint MethodId => 2u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).TransferBodyAsync(body);

    public void Serialize(ref CodecWriter writer)
    {
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.X));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Y));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Z));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.X));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Y));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Z));
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Mass));
    }
}

internal readonly struct ChunkBehavior_SeedInvokable(IReadOnlyList<Body> bodies) : IGrainVoidInvokable
{
    public uint MethodId => 3u;
    public ValueTask Invoke(IGrainBehavior behavior) => ((IChunkGrain)behavior).SeedAsync(bodies);

    public void Serialize(ref CodecWriter writer)
    {
        writer.WriteInt32(bodies.Count);
        foreach (Body body in bodies)
        {
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.X));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Y));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Position.Z));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.X));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Y));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Velocity.Z));
            writer.WriteFixed32(BitConverter.SingleToUInt32Bits(body.Mass));
        }
    }
}
