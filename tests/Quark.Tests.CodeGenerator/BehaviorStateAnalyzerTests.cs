using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.Analyzers;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class BehaviorStateAnalyzerTests
{
    // -----------------------------------------------------------------------
    // QRK0020 — mutable instance field
    // -----------------------------------------------------------------------

    [Fact]
    public void Reports_MutableField_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;
            using Quark.Core.Abstractions.Timers;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private IGrainTimer? _timer;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0020");
    }

    [Fact]
    public void Does_Not_Report_ReadonlyField_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private readonly string _name;
                public MyBehavior(string name) { _name = name; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0020");
    }

    [Fact]
    public void Does_Not_Report_ConstField_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private const int MaxRetries = 3;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0020");
    }

    [Fact]
    public void Does_Not_Report_StaticField_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private static int s_counter;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0020");
    }

    [Fact]
    public void Does_Not_Report_Field_On_NonBehavior_Class()
    {
        const string source = """
            namespace Demo;

            public sealed class SomeHelper
            {
                private int _count;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0020");
    }

    // -----------------------------------------------------------------------
    // QRK0021 — writable auto-property
    // -----------------------------------------------------------------------

    [Fact]
    public void Reports_WritableAutoProperty_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public string? Name { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0021");
    }

    [Fact]
    public void Does_Not_Report_InitOnlyAutoProperty_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public string Name { get; init; } = string.Empty;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0021");
    }

    [Fact]
    public void Does_Not_Report_ReadonlyAutoProperty_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                public string Name { get; } = string.Empty;
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0021");
    }

    [Fact]
    public void Does_Not_Report_ExplicitlyImplementedProperty_On_GrainBehavior()
    {
        const string source = """
            using Quark.Core.Abstractions.Grains;

            namespace Demo;

            public interface IMyGrain : IGrain { }

            public sealed class MyBehavior : IGrainBehavior, IMyGrain
            {
                private string _name = string.Empty;
                public string Name
                {
                    get => _name;
                    set => _name = value.Trim();
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new BehaviorStateAnalyzer());

        // The explicit property body means it is NOT an auto-property → no QRK0021.
        // The backing field _name IS mutable → QRK0020 fires instead.
        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0021");
        Assert.Contains(diagnostics, d => d.Id == "QRK0020");
    }
}
