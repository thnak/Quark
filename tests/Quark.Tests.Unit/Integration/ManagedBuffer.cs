namespace Quark.Tests.Unit.Integration;

public sealed class ManagedBuffer
{
    public int InitCount { get; set; }
    public int DestroyCount { get; set; }
    public string Data { get; set; } = string.Empty;
}