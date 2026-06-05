namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>A single versioned event record in the log.</summary>
public sealed class LogEntry
{
    public LogEntry(int version, object @event)
    {
        Version = version;
        Event = @event;
    }

    public int Version { get; }
    public object Event { get; }
}
