using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Transport;

/// <summary>
/// Tests for transport-layer abstractions and message envelope encoding.
/// </summary>
public sealed class MessageEnvelopeTests
{
    [Fact]
    public void MessageEnvelope_InitProperties()
    {
        byte[] payload = [1, 2, 3];
        MessageEnvelope envelope = new()
        {
            CorrelationId = 42,
            MessageType = MessageType.Request,
            Payload = payload,
        };

        Assert.Equal(42, envelope.CorrelationId);
        Assert.Equal(MessageType.Request, envelope.MessageType);
        Assert.Equal(payload, envelope.Payload.ToArray());
    }

    [Fact]
    public void MessageHeaders_SetAndGet()
    {
        MessageHeaders headers = new();
        headers.Set("trace-id", "abc-123");
        headers.Set("deadline", "2026-01-01");

        Assert.Equal("abc-123", headers.Get("trace-id"));
        Assert.Equal("2026-01-01", headers.Get("deadline"));
        Assert.Null(headers.Get("missing-key"));
    }

    [Fact]
    public void MessageHeaders_Overwrite()
    {
        MessageHeaders headers = new();
        headers.Set("key", "v1");
        headers.Set("key", "v2");
        Assert.Equal("v2", headers.Get("key"));
    }

    [Fact]
    public void MessageType_AllValuesDistinct()
    {
        byte[] values = [
            (byte)MessageType.Request,
            (byte)MessageType.Response,
            (byte)MessageType.OneWayRequest,
            (byte)MessageType.System,
        ];
        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
