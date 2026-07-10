using Quark.Streaming.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class SharedStreamItemTests
{
    [Fact]
    public void GetOrEncode_InvokesEncoderOnlyOnce_AndMemoizesBytes()
    {
        var item = new SharedStreamItem("payload");
        int calls = 0;
        ReadOnlyMemory<byte> Encode(object o)
        {
            calls++;
            return new byte[] { 1, 2, 3 };
        }

        ReadOnlyMemory<byte> first = item.GetOrEncode(Encode);
        ReadOnlyMemory<byte> second = item.GetOrEncode(Encode);

        Assert.Equal(1, calls);
        Assert.True(first.Span.SequenceEqual(new byte[] { 1, 2, 3 }));
        Assert.True(second.Span.SequenceEqual(first.Span));
    }

    [Fact]
    public void Item_ExposesRawItem()
    {
        var payload = new object();
        var item = new SharedStreamItem(payload);
        Assert.Same(payload, item.Item);
    }
}
