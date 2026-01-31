namespace Quark.Examples.ContextRegistration;

/// <summary>
/// Example interface from an "external library" that cannot be modified.
/// This interface does NOT inherit from IQuarkActor.
/// </summary>
public interface ICalculatorService
{
    string ActorId { get; }
    
    Task<int> AddAsync(int a, int b);
    Task<int> MultiplyAsync(int x, int y);
    Task<string> GetHistoryAsync();
}
