using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.Analyzers;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class DeterministicReplayAnalyzerTests
{
    private const string JournaledGrainScaffold = """
        using System.Threading.Tasks;
        using Quark.Core.Abstractions.Grains;
        using Quark.Core.Abstractions.Hosting;
        using Quark.Persistence.Abstractions.Journaling;

        namespace Demo;

        public sealed class MyState { public int Value; }
        public sealed class MyEvent { public int Amount; }

        public interface IMyGrain : IGrain
        {
            Task ApplyAsync(int amount);
        }
        """;

    [Fact]
    public void Reports_GuidNewGuid_In_TransitionState()
    {
        string source = JournaledGrainScaffold + """

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    RaiseEvent(new MyEvent { Amount = amount });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value = System.Guid.NewGuid().GetHashCode();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0041");
    }

    [Theory]
    [InlineData("System.DateTime.Now")]
    [InlineData("System.DateTime.UtcNow")]
    [InlineData("System.DateTime.Today")]
    [InlineData("System.DateTimeOffset.Now")]
    [InlineData("System.DateTimeOffset.UtcNow")]
    public void Reports_NondeterministicClockAccess_In_TransitionState(string clockExpression)
    {
        string source = JournaledGrainScaffold + $$"""

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    RaiseEvent(new MyEvent { Amount = amount });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value = {{clockExpression}}.GetHashCode();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0041");
    }

    [Fact]
    public void Reports_ParameterlessNewRandom_In_TransitionState()
    {
        string source = JournaledGrainScaffold + """

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    RaiseEvent(new MyEvent { Amount = amount });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value = new System.Random().Next();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0041");
    }

    [Fact]
    public void Does_Not_Report_SeededNewRandom_In_TransitionState()
    {
        string source = JournaledGrainScaffold + """

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    RaiseEvent(new MyEvent { Amount = amount });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value = new System.Random(42).Next();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0041");
    }

    [Fact]
    public void Does_Not_Report_PureTransitionState()
    {
        string source = JournaledGrainScaffold + """

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    RaiseEvent(new MyEvent { Amount = amount });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value += @event.Amount;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0041");
    }

    [Fact]
    public void Does_Not_Report_NondeterministicCall_Outside_TransitionState()
    {
        string source = JournaledGrainScaffold + """

            public sealed class MyGrain : JournaledGrain<MyState, MyEvent>, IMyGrain
            {
                public MyGrain(IActivationMemory<JournaledGrainState<MyState, MyEvent>> memory, ICallContext ctx)
                    : base(memory, ctx) { }

                public Task ApplyAsync(int amount)
                {
                    // Nondeterministic calls are fine outside TransitionState — e.g. computed
                    // before the event is raised and carried as part of the event payload.
                    System.Guid id = System.Guid.NewGuid();
                    RaiseEvent(new MyEvent { Amount = amount + id.GetHashCode() });
                    return Task.CompletedTask;
                }

                protected override void TransitionState(MyState state, MyEvent @event)
                {
                    state.Value += @event.Amount;
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0041");
    }

    [Fact]
    public void Does_Not_Report_NondeterministicCall_In_UnrelatedMethod_Named_TransitionState()
    {
        string source = """
            using System.Threading.Tasks;

            namespace Demo;

            public class NotAJournaledGrain
            {
                protected virtual void TransitionState(int state, int @event) { }
            }

            public sealed class Sub : NotAJournaledGrain
            {
                protected override void TransitionState(int state, int @event)
                {
                    int x = System.Guid.NewGuid().GetHashCode();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new DeterministicReplayAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0041");
    }
}
