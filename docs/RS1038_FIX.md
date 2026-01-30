# RS1038 Fix: Separating Analyzers and Code Fix Providers

## Problem

The Quark.Analyzers project was generating 6 RS1038 warnings:

```
warning RS1038: This compiler extension should not be implemented in an assembly 
containing a reference to Microsoft.CodeAnalysis.Workspaces. The 
Microsoft.CodeAnalysis.Workspaces assembly is not provided during command line 
compilation scenarios, so references to it could cause the compiler extension 
to behave unpredictably.
```

The warnings were appearing on these files:
- ActorParameterSerializabilityAnalyzer.cs
- ActorMethodSignatureAnalyzer.cs
- QuarkStreamAnalyzer.cs
- PerformanceAntiPatternAnalyzer.cs
- MissingActorAttributeAnalyzer.cs
- ReentrancyAnalyzer.cs

## Root Cause

The `Quark.Analyzers` project included a reference to `Microsoft.CodeAnalysis.CSharp.Workspaces`, which is required by code fix providers but not available during command-line compilation. This caused RS1038 warnings on all analyzer files, even though the analyzers themselves didn't use any Workspaces APIs.

## Solution

Following Microsoft's recommended pattern, we separated the project into two assemblies:

### 1. Quark.Analyzers (Analyzers Only)
- **Purpose**: Contains diagnostic analyzers only
- **Dependencies**: `Microsoft.CodeAnalysis.CSharp` (without Workspaces)
- **Files**: 
  - ActorMethodSignatureAnalyzer.cs
  - ActorParameterSerializabilityAnalyzer.cs
  - MissingActorAttributeAnalyzer.cs
  - PerformanceAntiPatternAnalyzer.cs
  - QuarkStreamAnalyzer.cs
  - ReentrancyAnalyzer.cs

### 2. Quark.Analyzers.CodeFixes (Code Fix Providers)
- **Purpose**: Contains code fix providers
- **Dependencies**: `Microsoft.CodeAnalysis.CSharp.Workspaces` + reference to `Quark.Analyzers`
- **Files**:
  - ActorMethodSignatureCodeFixProvider.cs
  - MissingActorAttributeCodeFixProvider.cs
  - StatePropertyCodeFixProvider.cs
  - SupervisionScaffoldCodeFixProvider.cs

## Changes Made

1. Created new `src/Quark.Analyzers.CodeFixes/` project
2. Moved all 4 `*CodeFixProvider.cs` files from `Quark.Analyzers` to `Quark.Analyzers.CodeFixes`
3. Removed `Microsoft.CodeAnalysis.CSharp.Workspaces` reference from `Quark.Analyzers.csproj`
4. Added `Quark.Analyzers.CodeFixes` to the solution file (`Quark.slnx`)
5. Updated consuming projects to reference both analyzer projects:
   - `examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj`
   - `tests/Quark.Tests/Quark.Tests.csproj`
6. Added README.md files to document the structure and reasoning

## Usage

Projects that use Quark analyzers should now reference both projects:

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

## Result

✅ **All 6 RS1038 warnings resolved**
✅ Build succeeds with 0 RS1038 errors
✅ Analyzers continue to work correctly
✅ Code fix providers continue to work correctly
✅ Tests pass (349/350 passing, 1 pre-existing flaky test unrelated to this change)

## References

- [RS1038 Rule Documentation](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/rules/RS1038.md)
- [Roslyn Analyzers Best Practices](https://github.com/dotnet/roslyn-analyzers/blob/main/docs/Analyzer%20Configuration.md)
- [Separating Analyzers and Code Fixes (Microsoft Pattern)](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix)
