using Quark.Transport.Abstractions;
using Quark.Runtime;
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
        GrainInvocationRequest request = new(
            new Quark.Core.Abstractions.GrainId(new Quark.Core.Abstractions.GrainType("CounterGrain"), "abc"),
            7u,
            new object?[] { 42, "hello", true });

        GrainMessageSerializer serializer = new();

        byte[] requestBytes = serializer.SerializeRequest(request);
        GrainInvocationRequest decodedRequest = serializer.DeserializeRequest(requestBytes);

        Assert.Equal(request.GrainId, decodedRequest.GrainId);
        Assert.Equal(request.MethodId, decodedRequest.MethodId);
        Assert.Equal(request.Arguments, decodedRequest.Arguments);

        GrainInvocationResponse response = new(true, 99L, null);
        byte[] responseBytes = serializer.SerializeResponse(response);
        GrainInvocationResponse decodedResponse = serializer.DeserializeResponse(responseBytes);

        Assert.True(decodedResponse.Success);
        Assert.Equal(99L, decodedResponse.Result);
        Assert.Null(decodedResponse.Error);
    }
}
