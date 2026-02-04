using ProtoBuf;

namespace Quark.Tests;

[ProtoContract]
public struct Info
{
    [ProtoMember(1)]
    public int Id { get; set; }
    
    [ProtoMember(2)]
    public string Description { get; set; }
}