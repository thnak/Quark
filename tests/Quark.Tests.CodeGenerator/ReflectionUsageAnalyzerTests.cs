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

    // -----------------------------------------------------------------------
    // QRK0004 — instance GetType() passed to a method argument
    // -----------------------------------------------------------------------

    [Fact]
    public void Reports_InstanceGetType_Passed_To_Method_Argument()
    {
        const string source = """
                              namespace Demo;

                              public interface ICodecProvider
                              {
                                  object? TryGetGeneralizedCodec(System.Type type);
                              }

                              public static class Dispatcher
                              {
                                  public static void Dispatch(object item, ICodecProvider codecs)
                                  {
                                      var codec = codecs.TryGetGeneralizedCodec(item.GetType());
                                  }
                              }
                              """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.Contains(diagnostics, d => d.Id == "QRK0004");
    }

    [Fact]
    public void Does_Not_Report_InstanceGetType_When_Result_Not_Passed_To_Method()
    {
        const string source = """
                              namespace Demo;

                              public static class Helper
                              {
                                  public static string GetTypeName(object item)
                                  {
                                      return item.GetType().Name;
                                  }
                              }
                              """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0004");
    }

    [Fact]
    public void Does_Not_Report_InstanceGetType_When_Method_Has_RequiresDynamicCode()
    {
        const string source = """
                              using System.Diagnostics.CodeAnalysis;

                              namespace Demo;

                              public interface ICodecProvider
                              {
                                  object? TryGetGeneralizedCodec(System.Type type);
                              }

                              public static class Dispatcher
                              {
                                  [RequiresDynamicCode("Codec dispatch uses runtime type.")]
                                  public static void Dispatch(object item, ICodecProvider codecs)
                                  {
                                      var codec = codecs.TryGetGeneralizedCodec(item.GetType());
                                  }
                              }
                              """;

        ImmutableArray<Diagnostic> diagnostics = AnalyzerTestDriver.Run(source, new ReflectionUsageAnalyzer());

        Assert.DoesNotContain(diagnostics, d => d.Id == "QRK0004");
    }
}
