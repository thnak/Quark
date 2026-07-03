using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.Analyzers;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class ReentrancyAnalyzerTests
{
    [Fact]
    public void Reports_AwaitedSelfInterfaceCall_On_NonReentrantBehavior()
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
                private readonly IMyGrain _self;
                public MyBehavior(IMyGrain self) { _self = self; }

                public async Task DoAsync()
                {
                    await _self.DoAsync();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0040");
    }

    [Fact]
    public void Does_Not_Report_When_Behavior_Is_Reentrant()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain
            {
                Task DoAsync();
            }

            [Reentrant]
            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private readonly IMyGrain _self;
                public MyBehavior(IMyGrain self) { _self = self; }

                public async Task DoAsync()
                {
                    await _self.DoAsync();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0040");
    }

    [Fact]
    public void Does_Not_Report_FireAndForget_SelfInterfaceCall()
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
                private readonly IMyGrain _self;
                public MyBehavior(IMyGrain self) { _self = self; }

                public Task DoAsync()
                {
                    _ = _self.DoAsync();
                    return Task.CompletedTask;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0040");
    }

    [Fact]
    public void Does_Not_Report_AwaitedCall_To_UnrelatedGrainInterface()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain
            {
                Task DoAsync();
            }

            public interface IOtherGrain : IGrain
            {
                Task OtherAsync();
            }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private readonly IOtherGrain _other;
                public MyBehavior(IOtherGrain other) { _other = other; }

                public async Task DoAsync()
                {
                    await _other.OtherAsync();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0040");
    }

    [Fact]
    public void Does_Not_Report_AwaitedCall_On_NonBehavior_Class()
    {
        const string source = """
            using System.Threading.Tasks;
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain
            {
                Task DoAsync();
            }

            public sealed class SomeHelper
            {
                private readonly IMyGrain _self;
                public SomeHelper(IMyGrain self) { _self = self; }

                public async Task RunAsync()
                {
                    await _self.DoAsync();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0040");
    }

    [Fact]
    public void Does_Not_Report_When_Task_Stored_In_Local_Before_Await()
    {
        // Known limitation: the heuristic only matches the direct `await ref.Method()` shape.
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
                private readonly IMyGrain _self;
                public MyBehavior(IMyGrain self) { _self = self; }

                public async Task DoAsync()
                {
                    Task pending = _self.DoAsync();
                    await pending;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReentrancyAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0040");
    }
}
