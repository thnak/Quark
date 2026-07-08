namespace Quark.Streaming.Abstractions;

public interface IImplicitStreamActivator
{
    ValueTask EnsureActivatedAsync(string grainTypeKey, string streamKey, CancellationToken cancellationToken = default);
}
