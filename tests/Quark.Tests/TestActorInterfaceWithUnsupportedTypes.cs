using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Quark.Abstractions;

namespace Quark.Tests;

/// <summary>
/// This interface intentionally uses unsupported types to test the QUARK017 analyzer.
/// These should all produce compilation errors when the analyzer is active.
/// </summary>
public interface ITestUnsupportedTypesActor : IQuarkActor
{
    // SHOULD ERROR: Action delegate
    // Task ProcessWithActionAsync(Action callback);
    
    // SHOULD ERROR: Func delegate
    // Task<int> ProcessWithFuncAsync(Func<int> callback);
    
    // SHOULD ERROR: Expression tree
    // Task ProcessWithExpressionAsync(Expression<Func<int, bool>> predicate);
    
    // SHOULD ERROR: IEnumerable (lazy evaluation)
    // Task ProcessIEnumerableAsync(IEnumerable<int> items);
    
    // SHOULD ERROR: IAsyncEnumerable (lazy evaluation)
    // Task ProcessIAsyncEnumerableAsync(IAsyncEnumerable<int> items);
    
    // SHOULD PASS: Concrete List (allowed)
    Task ProcessListAsync(List<int> items);
    
    // SHOULD PASS: Array (allowed)
    Task ProcessArrayAsync(int[] items);
    
    // SHOULD PASS: Concrete class
    Task ProcessConcreteTypeAsync(string name, int value);
}

/// <summary>
/// Custom delegate type - should also be detected as unsupported
/// </summary>
public delegate void CustomEventHandler(object sender, EventArgs e);

/// <summary>
/// Interface with custom delegate - should error
/// </summary>
public interface ITestCustomDelegateActor : IQuarkActor
{
    // SHOULD ERROR: Custom delegate
    // Task RegisterHandlerAsync(CustomEventHandler handler);
}
