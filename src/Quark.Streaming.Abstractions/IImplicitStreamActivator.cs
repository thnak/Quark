namespace Quark.Streaming.Abstractions;

public interface IImplicitStreamActivator
{
    Task EnsureActivatedAsync(string grainTypeKey, string streamKey, CancellationToken cancellationToken = default);
}
