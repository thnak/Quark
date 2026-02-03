using System.Threading.Channels;

namespace Quark.Networking.Abstractions;

public interface IQuarkChannelEnvelopeQueue
{
    public Channel<QuarkEnvelope> Outgoing { get; }
}