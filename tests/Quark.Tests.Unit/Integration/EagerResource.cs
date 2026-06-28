namespace Quark.Tests.Unit.Integration;

public sealed class EagerResource
{
    public string LoadedById { get; set; } = string.Empty;
    public int InitCount { get; set; }
    public int DestroyCount { get; set; }
    public bool ValueAvailableInOnActivate { get; set; }
}

public sealed class EagerScopedService
{
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
}
