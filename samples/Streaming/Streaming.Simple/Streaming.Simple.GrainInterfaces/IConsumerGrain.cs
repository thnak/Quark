using Quark.Core.Abstractions.Grains;
using Quark.Streaming.Abstractions;

namespace Streaming.Simple.GrainInterfaces;

public interface IConsumerGrain : IGrainWithGuidKey
{
    Task Subscribe(StreamId streamId);
    Task Unsubscribe();
}
