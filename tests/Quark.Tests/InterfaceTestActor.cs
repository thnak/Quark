using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Tests;

/// <summary>
/// Test actor that uses InterfaceType attribute.
/// </summary>
[Actor(InterfaceType = typeof(IInterfaceTestActor))]
public class InterfaceTestActor : ActorBase, IInterfaceTestActor
{
    public InterfaceTestActor(string actorId) : base(actorId) { }
    
    public Task<string> TestMethodAsync()
    {
        return Task.FromResult("test result");
    }
}