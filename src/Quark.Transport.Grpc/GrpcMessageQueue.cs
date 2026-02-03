using System.Threading.Channels;
using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

public class GrpcMessageQueue : IQuarkChannelEnvelopeQueue
{
    private readonly Channel<QuarkEnvelope> _outgoing;

    public GrpcMessageQueue()
    {
       

        _outgoing = Channel.CreateUnbounded<QuarkEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Channel<QuarkEnvelope> Outgoing => _outgoing;
}