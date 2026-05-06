using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Quark.CodeGenerator;
using Xunit;

namespace Quark.Tests.CodeGenerator;

public sealed class SerializerGeneratorTests
{
    [Fact]
    public void Generates_Codec_And_Copier_For_Attributed_Type()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class Person
                              {
                                  [Id(0)] public string? Name { get; set; }
                                  [Id(1)] public int Age { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        string generated = Assert.Single(result.GeneratedSources);
        Assert.Contains("internal sealed class PersonCodec", generated);
        Assert.Contains("internal sealed class PersonCopier", generated);
        Assert.Contains("if (value is null)", generated);
        Assert.Contains("if (field.WireType == global::Quark.Serialization.Abstractions.WireType.Extended", generated);
        Assert.Contains("case 0: result.Name =", generated);
        Assert.Contains("case 1: result.Age =", generated);
        Assert.Contains("private readonly global::Quark.Serialization.Abstractions.ICopierProvider _copiers;",
            generated);
        Assert.Contains("_copiers.GetRequiredCopier<", generated);
        Assert.Contains("DeepCopy(input.Name, context)", generated);
    }

    [Fact]
    public void Ignores_Types_Without_Id_Members()
    {
        const string source = """
                              using Quark.Serialization.Abstractions.Attributes;

                              namespace Demo;

                              [GenerateSerializer]
                              public sealed class EmptyPayload
                              {
                                  public string? Name { get; set; }
                              }
                              """;

        GeneratorTestResult result = GeneratorTestDriver.Run(source, new SerializerGenerator());

        AssertNoErrors(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    private static void AssertNoErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
