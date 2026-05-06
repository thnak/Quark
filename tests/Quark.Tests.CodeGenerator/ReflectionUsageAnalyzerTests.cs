using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.Analyzers;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class ReflectionUsageAnalyzerTests
{
    [Fact]
    public void Reports_DynamicAssemblyLoad()
    {
        const string source = """
                              using System.Reflection;

                              namespace Demo;

                              public static class Loader
                              {
                                  public static Assembly LoadIt() => Assembly.Load("Demo.Plugin");
                              }
                              """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0002");
    }

    [Fact]
    public void Reports_ISerializable_Implementation()
    {
        const string source = """
                              using System;
                              using System.Runtime.Serialization;

                              namespace Demo;

                              public sealed class LegacyPayload : ISerializable
                              {
                                  public void GetObjectData(SerializationInfo info, StreamingContext context) { }
                              }
                              """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0003");
    }
}