# Quark.Analyzers.CodeFixes

This project contains code fix providers for the Quark analyzer diagnostics.

## Why a Separate Project?

Code fix providers require `Microsoft.CodeAnalysis.CSharp.Workspaces`, which is not available during command-line compilation scenarios. To comply with **RS1038** (compiler extensions should not reference assemblies unavailable in all compilation scenarios), we separate:

- **Quark.Analyzers**: Contains analyzers only (no Workspaces dependency)
- **Quark.Analyzers.CodeFixes**: Contains code fix providers (with Workspaces dependency)

This is the recommended pattern by the Roslyn team for building analyzers and code fixes.

## Code Fix Providers

This project contains the following code fix providers:

1. **ActorMethodSignatureCodeFixProvider**: Converts synchronous actor methods to async
2. **MissingActorAttributeCodeFixProvider**: Adds missing `[Actor]` attribute
3. **StatePropertyCodeFixProvider**: Generates state properties with `[QuarkState]` attribute
4. **SupervisionScaffoldCodeFixProvider**: Scaffolds `ISupervisor` implementation

## Usage

When referencing the analyzers in consuming projects, include both projects:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Analyzers/Quark.Analyzers.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
  <ProjectReference Include="path/to/Quark.Analyzers.CodeFixes/Quark.Analyzers.CodeFixes.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## References

- [RS1038: Compiler extensions should be implemented in assemblies with compiler-provided references](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/rules/RS1038.md)
- [Roslyn Analyzers Best Practices](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/Analyzer%20Configuration.md)
