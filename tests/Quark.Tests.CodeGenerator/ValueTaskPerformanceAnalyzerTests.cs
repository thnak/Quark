using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Quark.Analyzers;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class ValueTaskPerformanceAnalyzerTests
{
    // -----------------------------------------------------------------------
    // QRK0030 — Task return type on IGrainBehavior method
    // -----------------------------------------------------------------------

    [Fact]
    public void Qrk0030_Fires_On_NonAsync_Task_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task DoAsync() => Task.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.Contains(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Fires_On_NonAsync_TaskOfT_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task<int> GetAsync() => Task.FromResult(42);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.Contains(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Fires_On_Async_Task_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public async Task DoAsync() { await Task.Yield(); }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.Contains(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Does_Not_Fire_On_ValueTask_Return()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public ValueTask DoAsync() => ValueTask.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Does_Not_Fire_On_NonBehavior_Class()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class SomeHelper
            {
                public Task DoAsync() => Task.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Does_Not_Fire_On_Abstract_Method()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public abstract class MyBehaviorBase : IGrainBehavior, IMyGrain
            {
                public abstract Task DoAsync();
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0030");
    }

    [Fact]
    public void Qrk0030_Does_Not_Fire_On_Explicit_Interface_Implementation()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain
            {
                Task DoAsync();
            }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                Task IMyGrain.DoAsync() => Task.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0030");
    }

    // -----------------------------------------------------------------------
    // QRK0030 fixer tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Qrk0030_Fix_Task_CompletedTask_ExpressionBody()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task DoAsync() => Task.CompletedTask;
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("ValueTask DoAsync()", result);
        Assert.Contains("ValueTask.CompletedTask", result);
        // "ValueTask.CompletedTask" contains "Task.CompletedTask" as a substring,
        // so check the arrow expression specifically.
        Assert.DoesNotContain("=> Task.CompletedTask", result);
    }

    [Fact]
    public async Task Qrk0030_Fix_Task_FromResult_ExpressionBody()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task<long> GetAsync() => Task.FromResult(42L);
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("ValueTask<long>", result);
        Assert.Contains("new ValueTask<long>(42L)", result);
    }

    [Fact]
    public async Task Qrk0030_Fix_Async_Task_Changes_ReturnType_Only()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public async Task DoAsync()
                {
                    await Task.Yield();
                }
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("async ValueTask DoAsync()", result);
        Assert.Contains("await Task.Yield();", result);
    }

    [Fact]
    public async Task Qrk0030_Fix_Task_CompletedTask_BlockBody()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task DoAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("ValueTask DoAsync()", result);
        Assert.Contains("return ValueTask.CompletedTask;", result);
    }

    [Fact]
    public async Task Qrk0030_Fix_BlockBody_Wraps_NonFactory_Return()
    {
        // The returned expression is a plain Task<int>-typed local, not a
        // Task.CompletedTask/Task.FromResult call — the rewriter must wrap it
        // in `new ValueTask<int>(...)` rather than leaving it untouched.
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task<int> GetAsync()
                {
                    Task<int> inner = Helper.ComputeAsync();
                    return inner;
                }
            }

            public static class Helper
            {
                public static Task<int> ComputeAsync() => Task.FromResult(42);
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("ValueTask<int> GetAsync()", result);
        Assert.Contains("new ValueTask<int>(inner)", result);
    }

    [Fact]
    public async Task Qrk0030_Fix_BlockBody_Handles_Multiple_Return_Statements()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public Task<int> GetAsync(bool flag)
                {
                    if (flag)
                    {
                        return Task.FromResult(1);
                    }

                    return Task.FromResult(2);
                }
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0030");

        Assert.Contains("ValueTask<int> GetAsync(bool flag)", result);
        Assert.Contains("new ValueTask<int>(1)", result);
        Assert.Contains("new ValueTask<int>(2)", result);
    }

    // -----------------------------------------------------------------------
    // QRK0031 — Task.CompletedTask / Task.FromResult inside ValueTask method
    // -----------------------------------------------------------------------

    [Fact]
    public void Qrk0031_Fires_On_TaskCompletedTask_In_ValueTask_Method()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public ValueTask DoAsync() => Task.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.Contains(diagnostics, d => d.Id == "QRK0031");
    }

    [Fact]
    public void Qrk0031_Fires_On_TaskFromResult_In_ValueTaskOfT_Method()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public ValueTask<int> GetAsync() => Task.FromResult(1);
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.Contains(diagnostics, d => d.Id == "QRK0031");
    }

    [Fact]
    public void Qrk0031_Does_Not_Fire_Inside_Task_Method()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public Task DoAsync() => Task.CompletedTask;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0031");
    }

    [Fact]
    public void Qrk0031_Does_Not_Fire_Inside_Lambda_Nested_In_ValueTask_Method()
    {
        // IsInsideValueTaskMethod stops walking up at lambda boundaries, so a
        // Task.CompletedTask captured inside a Func<Task> local should not be
        // attributed to the enclosing ValueTask-returning method.
        const string source = """
            using System;
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public ValueTask DoAsync()
                {
                    Func<Task> factory = () => Task.CompletedTask;
                    factory();
                    return ValueTask.CompletedTask;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ValueTaskPerformanceAnalyzer());
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0031");
    }

    // -----------------------------------------------------------------------
    // QRK0031 fixer tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Qrk0031_Fix_TaskCompletedTask_To_ValueTaskCompletedTask()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public ValueTask DoAsync() => Task.CompletedTask;
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0031");

        Assert.Contains("ValueTask.CompletedTask", result);
        Assert.DoesNotContain("=> Task.CompletedTask", result);
    }

    [Fact]
    public async Task Qrk0031_Fix_TaskFromResult_To_ValueTaskFromResult()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public sealed class MyService
            {
                public ValueTask<int> GetAsync() => Task.FromResult(42);
            }
            """;

        string result = await CodeFixTestDriver.ApplyFixAsync(
            source,
            new ValueTaskPerformanceAnalyzer(),
            new ValueTaskPerformanceFixer(),
            "QRK0031");

        Assert.Contains("ValueTask.FromResult(42)", result);
        // "ValueTask.FromResult" contains "Task.FromResult" as a substring; check the arrow form.
        Assert.DoesNotContain("=> Task.FromResult", result);
    }
}
