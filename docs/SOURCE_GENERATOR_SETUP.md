# Source Generator Setup Guide

## Overview

Quark uses Roslyn source generators to create actor factory methods at compile time, eliminating the need for reflection. This ensures full AOT (Ahead-of-Time) compatibility.

## Important: Analyzer References Are Not Transitive

When using the Quark framework, you **must** explicitly reference the `Quark.Generators` project as an Analyzer in your project, even though you're already referencing `Quark.Core`.

### Why?

Roslyn analyzers and source generators do not propagate transitively through project references. While `Quark.Core` includes the generator reference, it doesn't automatically flow to consuming projects.

## How to Set Up Your Project

Add the following to your `.csproj` file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference Quark.Core for the framework -->
    <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
    
    <!-- REQUIRED: Reference the source generator explicitly -->
    <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                      OutputItemType="Analyzer" 
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

## What Happens Without This Reference?

If you forget to add the generator reference, you'll get a runtime error:

```
System.InvalidOperationException: No factory registered for actor type YourActor. 
Ensure the actor is marked with [Actor] attribute for source generation.
```

Even though your actor class has the `[Actor]` attribute, the factory method won't be generated without the explicit generator reference.

## Verifying the Generator Is Working

After adding the reference and building your project, you should see:

1. **No errors** when building
2. **Generated files** in your `obj/Debug/net10.0/` directory (look for files with names like `YourActorFactory.g.cs`)
3. **Warnings** about `ActorAttribute` conflicts (these are harmless and can be ignored)
4. Your actors should **work at runtime** without reflection

## Examples

See the working examples in the repository:
- `examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj`
- `examples/Quark.Examples.Supervision/Quark.Examples.Supervision.csproj`
- `tests/Quark.Tests/Quark.Tests.csproj`

## Troubleshooting

### Problem: Actor not found at runtime

**Solution**: Make sure you've added the explicit generator reference as shown above.

### Problem: Generator not producing files

**Solution**: 
1. Clean your project: `dotnet clean`
2. Rebuild: `dotnet build`
3. Check that the generator reference has `OutputItemType="Analyzer"`

### Problem: Multiple attribute definition warnings

**Cause**: The source generator creates its own copy of the `ActorAttribute` for each project.

**Solution**: These warnings are harmless and can be safely ignored. They don't affect functionality.
