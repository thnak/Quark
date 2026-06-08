using Quark.Core.Abstractions.Grains;

namespace Streaming.Simple.GrainInterfaces;

public interface IProducerGrain : IGrainWithStringKey
{
    Task StartProducing(string ns, Guid key);
    Task StopProducing();
}
