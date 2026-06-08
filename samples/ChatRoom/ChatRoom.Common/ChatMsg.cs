using Quark.Serialization.Abstractions.Attributes;

namespace ChatRoom.Common;

[GenerateSerializer]
[Alias("ChatMsg")]
public sealed class ChatMsg
{
    [Id(0)] public string Author { get; set; } = "";
    [Id(1)] public string Text { get; set; } = "";
    [Id(2)] public DateTimeOffset Created { get; set; }
}
