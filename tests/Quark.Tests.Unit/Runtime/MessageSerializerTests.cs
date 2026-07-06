using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class MessageSerializerTests
{
    [Fact]
    public void MessageEnvelope_RoundTrips_With_Headers_And_Payload()
    {
        MessageHeaders headers = new();
        headers.Set("grain-type", "CounterGrain");
        headers.Set("grain-key", "cart-42");

        MessageEnvelope envelope = new()
        {
            CorrelationId = 123,
            MessageType = MessageType.Request,
            Headers = headers,
            Payload = new byte[] { 1, 2, 3, 4 }
        };

        MessageSerializer serializer = new();
        byte[] bytes = serializer.Serialize(envelope);
        MessageEnvelope roundTrip = serializer.Deserialize(bytes);

        Assert.Equal(envelope.CorrelationId, roundTrip.CorrelationId);
        Assert.Equal(envelope.MessageType, roundTrip.MessageType);
        Assert.Equal("CounterGrain", roundTrip.Headers?.Get("grain-type"));
        Assert.Equal("cart-42", roundTrip.Headers?.Get("grain-key"));
        Assert.Equal(envelope.Payload.ToArray(), roundTrip.Payload.ToArray());
    }

    [Fact]
    public void GrainInvocation_Request_And_Response_RoundTrip()
    {
        ReadOnlyMemory<byte> argPayload = GrainMessageSerializer.SerializeArgs(42, "hello", true);
        GrainInvocationRequest request = new(
            new GrainId(new GrainType("CounterGrain"), "abc"),
            7u,
            argPayload);

        GrainMessageSerializer serializer = new();

        byte[] requestBytes = serializer.SerializeRequest(request);
        GrainInvocationRequest decodedRequest = serializer.DeserializeRequest(requestBytes);

        Assert.Equal(request.GrainId, decodedRequest.GrainId);
        Assert.Equal(request.MethodId, decodedRequest.MethodId);
        Assert.Equal(request.ArgumentPayload.ToArray(), decodedRequest.ArgumentPayload.ToArray());

        var resultBuf = new ArrayBufferWriter<byte>();
        var resultWriter = new CodecWriter(resultBuf);
        resultWriter.WriteInt64(99L);
        GrainInvocationResponse response = new(true, resultBuf.WrittenMemory.ToArray(), null);
        byte[] responseBytes = serializer.SerializeResponse(response);
        GrainInvocationResponse decodedResponse = serializer.DeserializeResponse(responseBytes);

        Assert.True(decodedResponse.Success);
        var resultReader = new CodecReader(decodedResponse.ResultPayload);
        Assert.Equal(99L, resultReader.ReadInt64());
        Assert.Null(decodedResponse.Error);
    }

    [Fact]
    public void SerializeRequest_ComponentsOverload_ProducesIdenticalBytesToRecordOverload()
    {
        var grainId = new GrainId(new GrainType("CounterGrain"), "key-99");
        uint methodId = 3u;
        ReadOnlyMemory<byte> argPayload = GrainMessageSerializer.SerializeArgs(100, "world");

        GrainMessageSerializer serializer = new();

        byte[] fromRecord = serializer.SerializeRequest(
            new GrainInvocationRequest(grainId, methodId, argPayload));
        byte[] fromComponents = serializer.SerializeRequest(grainId, methodId, argPayload.Span);

        Assert.Equal(fromRecord, fromComponents);
    }

    [Fact]
    public void SerializeRequest_ComponentsOverload_RoundTrips()
    {
        var grainId = new GrainId(new GrainType("InventoryGrain"), "warehouse-7");
        uint methodId = 5u;
        ReadOnlyMemory<byte> argPayload = GrainMessageSerializer.SerializeArgs(42L, false);

        GrainMessageSerializer serializer = new();

        byte[] bytes = serializer.SerializeRequest(grainId, methodId, argPayload.Span);
        GrainInvocationRequest decoded = serializer.DeserializeRequest(bytes);

        Assert.Equal(grainId, decoded.GrainId);
        Assert.Equal(methodId, decoded.MethodId);
        Assert.Equal(argPayload.ToArray(), decoded.ArgumentPayload.ToArray());
    }

    [Fact]
    public void SerializeRequest_ComponentsOverload_EmptyArgs_RoundTrips()
    {
        var grainId = new GrainId(new GrainType("PingGrain"), "srv");
        uint methodId = 1u;

        GrainMessageSerializer serializer = new();

        byte[] bytes = serializer.SerializeRequest(grainId, methodId, ReadOnlySpan<byte>.Empty);
        GrainInvocationRequest decoded = serializer.DeserializeRequest(bytes);

        Assert.Equal(grainId, decoded.GrainId);
        Assert.Equal(methodId, decoded.MethodId);
        Assert.Empty(decoded.ArgumentPayload.ToArray());
    }

    [Fact]
    public async Task ReadAsync_Rejects_Negative_Frame_Length()
    {
        Pipe pipe = new();
        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, -1);
        await pipe.Writer.WriteAsync(prefix);
        await pipe.Writer.CompleteAsync();

        MessageSerializer serializer = new();

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await serializer.ReadAsync(pipe.Reader));
    }

    [Fact]
    public async Task ReadAsync_Rejects_Frame_Larger_Than_Configured_Max()
    {
        Pipe pipe = new();
        byte[] frame = new byte[sizeof(int) + 9];
        BinaryPrimitives.WriteInt32LittleEndian(frame, 9);
        await pipe.Writer.WriteAsync(frame);
        await pipe.Writer.CompleteAsync();

        MessageSerializer serializer = new(new TransportOptions { MaxMessageBytes = 8 });

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await serializer.ReadAsync(pipe.Reader));
    }

    [Fact]
    public void Deserialize_Rejects_Too_Many_Headers()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new CodecWriter(buffer);
        writer.WriteInt64(1);
        writer.WriteByte((byte)MessageType.Request);
        writer.WriteVarUInt32(MessageSerializer.DefaultMaxHeaders + 1u);

        MessageSerializer serializer = new();

        Assert.Throws<InvalidDataException>(() => serializer.Deserialize(buffer.WrittenMemory));
    }

    [Fact]
    public void Deserialize_SingleSegment_Sequence_Produces_Same_Result_As_Memory_Overload()
    {
        MessageSerializer serializer = new();
        MessageEnvelope original = new()
        {
            CorrelationId = 99,
            MessageType = MessageType.Response,
            Payload = new byte[] { 10, 20, 30, 40 }
        };

        byte[] bytes = serializer.Serialize(original);
        ReadOnlySequence<byte> sequence = new(bytes);

        MessageEnvelope fromSequence = serializer.Deserialize(in sequence);
        MessageEnvelope fromMemory = serializer.Deserialize(bytes);

        Assert.Equal(fromMemory.CorrelationId, fromSequence.CorrelationId);
        Assert.Equal(fromMemory.MessageType, fromSequence.MessageType);
        Assert.Equal(fromMemory.Payload.ToArray(), fromSequence.Payload.ToArray());
    }

    [Fact]
    public void Deserialize_MultiSegment_Sequence_Produces_Same_Result_As_Memory_Overload()
    {
        MessageSerializer serializer = new();
        MessageEnvelope original = new()
        {
            CorrelationId = 77,
            MessageType = MessageType.OneWayRequest,
            Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }
        };

        byte[] bytes = serializer.Serialize(original);

        int splitAt = bytes.Length / 2;
        MemorySegment<byte> firstSegment = new(bytes.AsMemory(0, splitAt));
        MemorySegment<byte> lastSegment = firstSegment.Append(bytes.AsMemory(splitAt));
        ReadOnlySequence<byte> sequence = new(firstSegment, 0, lastSegment, lastSegment.Memory.Length);

        Assert.False(sequence.IsSingleSegment, "Sequence must be multi-segment for this test to exercise the fallback path.");

        MessageEnvelope fromSequence = serializer.Deserialize(in sequence);
        MessageEnvelope fromMemory = serializer.Deserialize(bytes);

        Assert.Equal(fromMemory.CorrelationId, fromSequence.CorrelationId);
        Assert.Equal(fromMemory.MessageType, fromSequence.MessageType);
        Assert.Equal(fromMemory.Payload.ToArray(), fromSequence.Payload.ToArray());
    }

    private sealed class MemorySegment<T> : ReadOnlySequenceSegment<T>
    {
        public MemorySegment(ReadOnlyMemory<T> memory) => Memory = memory;

        public MemorySegment<T> Append(ReadOnlyMemory<T> memory)
        {
            MemorySegment<T> next = new(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
