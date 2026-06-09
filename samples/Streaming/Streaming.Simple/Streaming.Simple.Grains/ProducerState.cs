using Quark.Core.Abstractions.Timers;
using Quark.Streaming.Abstractions;

namespace Streaming.Simple.Grains;

public sealed class ProducerState
{
    public IAsyncStream<int>? Stream { get; set; }
    public IGrainTimer? Timer { get; set; }
    public int Counter { get; set; }
}