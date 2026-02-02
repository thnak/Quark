using Quark.Abstractions;
using ProtoBuf;

namespace Quark.Tests;

/// <summary>
/// Test actor interface for proxy generation validation.
/// </summary>
public interface ITestProxyActor : IQuarkActor
{
    /// <summary>
    /// Increments a counter by the specified amount.
    /// </summary>
    Task IncrementAsync(int amount);

    /// <summary>
    /// Gets the current counter value.
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Processes a message and returns a response.
    /// </summary>
    Task<string> ProcessMessageAsync(string message, int priority);

    /// <summary>
    /// Processes a complex object and returns a modified object.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    Task<ObjectClassValue> ProcessComplexObjectAsync(ObjectClassValue input);
    
    /// <summary>
    /// Performs a reset operation without return value.
    /// </summary>
    Task ResetAsync();
}


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

[ProtoContract]
public struct Info
{
    [ProtoMember(1)]
    public int Id { get; set; }
    
    [ProtoMember(2)]
    public string Description { get; set; }
}