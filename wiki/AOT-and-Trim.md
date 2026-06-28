# AOT and Trim

Quark is designed for Native AOT from the ground up. Every production package has `IsTrimmable=true` and `EnableAotAnalyzer=true` set in `Directory.Build.props`.

## Rules for new code

### 1. Prefer source generation over reflection

The code generators (`GrainProxyGenerator`, `BehaviorRegistrationGenerator`, `SerializerGenerator`) emit all proxy, serializer, copier, and registration code at build time. New features should integrate with the generators rather than resolving types at runtime.

### 2. Annotate unavoidable dynamic calls

Any code that must use runtime reflection, `Assembly.Load`, `DynamicMethod`, or similar must be annotated:

```csharp
[RequiresUnreferencedCode("This path uses runtime type resolution.")]
[RequiresDynamicCode("This path emits IL at runtime.")]
public void LoadPlugin(string typeName)
{
    var type = Type.GetType(typeName); // only reachable on JIT
}
```

### 3. Guard JIT-only paths

Where a fast JIT path exists alongside a slower AOT-safe fallback:

```csharp
if (RuntimeFeature.IsDynamicCodeSupported)
{
    // Fast JIT path — eliminated by AOT compiler as dead code
    return EmitFastAccessor(member);
}
// AOT-safe fallback
return new ReflectionAccessor(member);
```

### 4. Use `[UnsafeAccessor]` for private member access

On .NET 8+, `[UnsafeAccessor]` is fully AOT-compatible and supersedes `DynamicMethod`-based private member access:

```csharp
[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_count")]
private static extern ref int GetCount(CounterState instance);
```

### 5. Never introduce `ISerializable`

`ISerializable` requires `DynamicMethod` and is incompatible with Native AOT (triggers analyzer `QRK0003`). Use `[GenerateSerializer]` instead.

### 6. Explicit provider registration

Auto-discovery via assembly scanning is not trim-safe. All providers — storage, reminder, stream, transport — must be registered with explicit extension methods.

## AOT analyzers

`Quark.Analyzers` ships four AOT analyzers that run during every build:

| Code | Rule | Severity |
|---|---|---|
| `QRK0001` | Dynamic type usage (`Type.GetType`, `MakeGenericType`, etc.) | Warning |
| `QRK0002` | `Assembly.Load` / `Assembly.LoadFrom` | Warning |
| `QRK0003` | `ISerializable`-based patterns | Error |
| `QRK0004` | Instance `object.GetType()` whose result flows into a method argument — the runtime codec-dispatch pattern (e.g. `_codecs.TryGetGeneralizedCodec(item.GetType())`), which defeats trimming/AOT | Warning |

These analyzers fire on any code in packages that set `EnableAotAnalyzer=true`.

> `Quark.Analyzers` also ships data-isolation (`QRK0010`–`QRK0012`) and behavior-lifecycle
> (`QRK0020`–`QRK0021`) analyzers; see their release notes in `AnalyzerReleases.Unshipped.md`.

## Native AOT smoke build

Use an OS-matching RID — cross-OS native compilation is not supported.

```bash
# Linux
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj \
    -f net10.0 -c Release -r linux-x64 /p:PublishAot=true

# Windows
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj \
    -f net10.0 -c Release -r win-x64 /p:PublishAot=true
```

## Package-level configuration

`Directory.Build.props` applies to all `src/` packages:

```xml
<PropertyGroup>
  <IsTrimmable>true</IsTrimmable>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <TrimmerRootDescriptor>$(MSBuildThisFileDirectory)TrimmerRoots.xml</TrimmerRootDescriptor>
</PropertyGroup>
```

## Common pitfalls

| Pattern | Problem | Fix |
|---|---|---|
| `Type.GetType(string)` | Runtime type resolution fails under trim | Use source-generated registries |
| `Activator.CreateInstance<T>()` without constraints | Allocator removed by linker | Use `new T()` with `where T : new()` |
| Unbound generic type stored in a `Dictionary<Type, ...>` | Generic instantiation lost | Emit explicit generic registrations at build time |
| `JsonSerializer.Serialize(obj)` without `JsonSerializerContext` | Reflection-based serialization | Use `[JsonSerializable]` source generator |
| `DynamicMethod` or `Reflection.Emit` | Not supported by AOT | Use `[UnsafeAccessor]` or source generation |

## Test the AOT publish

Run the smoke build as a CI gate. A successful `dotnet publish /p:PublishAot=true` means:
- No trim warnings
- No AOT analyzer warnings treated as errors
- The binary links and starts
