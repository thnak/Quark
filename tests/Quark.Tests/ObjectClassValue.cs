using ProtoBuf;

namespace Quark.Tests;

[ProtoContract]
public class ObjectClassValue
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;
    
    [ProtoMember(2)]
    public int Value { get; set; }
    
    [ProtoMember(3)]
    public DateTime Time { get; set; }
    
    [ProtoMember(4)]
    public List<string> Tags { get; set; } = new List<string>();
    
    [ProtoMember(5)]
    public List<Info> Infos { get; set; } = new List<Info>();
    
    [ProtoMember(6)]
    public List<string> EmptyList { get; set; } = new List<string>();
}