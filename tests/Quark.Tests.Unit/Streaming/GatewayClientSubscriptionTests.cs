using System.Buffers;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Streaming.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class GatewayClientSubscriptionTests
{
    [Fact]
    public async Task OnNextSharedAsync_EncodesOnce_AcrossSubscribersSharingItem()
    {
        var codec = new CountingCodec();
        var provider = new FakeCodecProvider(codec);
        var pushes = new List<byte[]>();
        Task Push(ReadOnlyMemory<byte> bytes, StreamSequenceToken? _)
        {
            pushes.Add(bytes.ToArray());
            return Task.CompletedTask;
        }

        StreamId streamId = StreamId.Create("ns", "key");
        var sub1 = new GatewayClientSubscription(Guid.NewGuid(), streamId, provider, Push);
        var sub2 = new GatewayClientSubscription(Guid.NewGuid(), streamId, provider, Push);
        var shared = new SharedStreamItem("payload");

        await sub1.OnNextSharedAsync(shared, null);
        await sub2.OnNextSharedAsync(shared, null);

        Assert.Equal(1, codec.WriteCount);           // serialized once
        Assert.Equal(2, pushes.Count);               // pushed to both subscribers
        Assert.Equal(pushes[0], pushes[1]);          // identical bytes
        Assert.Equal(new byte[] { 0x42 }, pushes[0]);
    }

    [Fact]
    public async Task OnNextAsync_DelegatesToSharedPath_AndEncodes()
    {
        var codec = new CountingCodec();
        var provider = new FakeCodecProvider(codec);
        byte[]? pushed = null;
        Task Push(ReadOnlyMemory<byte> bytes, StreamSequenceToken? _)
        {
            pushed = bytes.ToArray();
            return Task.CompletedTask;
        }

        var sub = new GatewayClientSubscription(Guid.NewGuid(), StreamId.Create("ns", "key"), provider, Push);

        await sub.OnNextAsync("payload", null);

        Assert.Equal(1, codec.WriteCount);
        Assert.Equal(new byte[] { 0x42 }, pushed);
    }

    private sealed class CountingCodec : IGeneralizedCodec
    {
        public int WriteCount;
        public bool IsSupportedType(Type type) => true;
        public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, object? value)
        {
            WriteCount++;
            writer.WriteByte(0x42);
        }
        public object? ReadValue(CodecReader reader, Field field) => throw new NotSupportedException();
    }

    private sealed class FakeCodecProvider(IGeneralizedCodec codec) : ICodecProvider
    {
        public IFieldCodec<T>? TryGetCodec<T>() => null;
        public IFieldCodec<T> GetRequiredCodec<T>() => throw new NotSupportedException();
        public IGeneralizedCodec? TryGetGeneralizedCodec(Type type) => codec;
    }
}
