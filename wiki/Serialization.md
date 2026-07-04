# Serialization

Quark uses a custom binary serialization format built on ZigZag + LEB128 variable-length encoding. Serialization is needed for any type that crosses a TCP grain call boundary. In-process calls never serialize — types are passed as-is.

## Annotating types

Apply `[GenerateSerializer]` to your type and tag each field or property you want serialized with `[Id(uint)]`:

```csharp
[GenerateSerializer]
public sealed class ChatMsg
{
    [Id(0)] public string Author { get; set; } = "";
    [Id(1)] public string Text   { get; set; } = "";
    [Id(2)] public DateTimeOffset Created { get; set; }
}
```

`[Id]` values must be **stable across versions** — never reuse or renumber them. Removing a field is fine (the codec skips unknown ids). Adding a new field gets a new id.

### Records

```csharp
[GenerateSerializer]
public sealed record Point([property: Id(0)] int X, [property: Id(1)] int Y);
```

### Inheritance

```csharp
[GenerateSerializer]
public abstract class ShapeBase { }

[GenerateSerializer]
[Alias("circle")]
public sealed class Circle : ShapeBase
{
    [Id(0)] public double Radius { get; set; }
}
```

### Type aliases

`[Alias("name")]` provides a stable string alias used for polymorphic type identity. Useful when the namespace or class name changes across versions:

```csharp
[GenerateSerializer]
[Alias("com.example.player-event/v1")]
public sealed class PlayerEvent { ... }
```

## Source generator

`SerializerGenerator` runs as part of `Quark.CodeGenerator`. For every type annotated `[GenerateSerializer]` it emits:
- `IFieldCodec<T>` — binary read/write
- `IDeepCopier<T>` — deep copy for the message dispatch path

No runtime reflection is used in the generated code. The generator emits explicit field accessors and type references, making the output fully AOT-safe.

## Registering codecs

Generated codecs are registered automatically when you use `AddQuarkSerialization()`:

```csharp
silo.Services.AddQuarkSerialization();
```

For custom stream item types that need explicit codec registration:

```csharp
silo.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
```

This registers both the codec and the copier under the type's identity.

## Primitive codecs

The following primitives have built-in codecs:

`bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `string`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `Guid`

`List<T>`, `Dictionary<K,V>`, `T[]`, and the six `System.Collections.Immutable` shapes are supported as `[GenerateSerializer]` DTO members — see [Collection support](#collection-support) below. There are no standalone, generically-usable `IFieldCodec<List<T>>`-style wrapper codecs; the generator emits the read/write logic per DTO member instead.

## Wire format

Each serialized field is written as:
1. Field tag — `(id << 3) | wireType` encoded as LEB128
2. Value — encoding depends on wire type:
   - Fixed-width (4 or 8 bytes) for floats
   - ZigZag-encoded LEB128 for signed integers
   - Length-prefixed for strings, bytes, embedded messages

The reader skips unknown field ids, so adding new fields to a type is forwards-compatible.

## CodecWriter / CodecReader

Low-level API for writing custom codecs without the source generator:

```csharp
public sealed class PointCodec : IFieldCodec<Point>
{
    public void WriteField<TBufferWriter>(
        ref Writer<TBufferWriter> writer,
        uint fieldIdDelta,
        Type expectedType,
        Point value)
        where TBufferWriter : IBufferWriter<byte>
    {
        ReferenceCodec.MarkValueField(ref writer.Session);
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(Point), WireType.TagDelimited);
        Int32Codec.WriteField(ref writer, 0, value.X);
        Int32Codec.WriteField(ref writer, 1, value.Y);
        writer.WriteEndObject();
    }

    public Point ReadValue<TInput>(ref Reader<TInput> reader, Field field)
    {
        int x = 0, y = 0;
        Field f;
        while (true)
        {
            f = reader.ReadFieldHeader();
            if (f.IsEndBaseOrEndObject) break;
            switch (f.FieldIdDelta)
            {
                case 0: x = Int32Codec.ReadValue(ref reader, f); break;
                case 1: y = Int32Codec.ReadValue(ref reader, f); break;
                default: reader.ConsumeUnknownField(f); break;
            }
        }
        return new Point(x, y);
    }
}
```

## Collection support

`[GenerateSerializer]` DTOs support the following collection member types out of the box — no extra registration needed:

| Type | Notes |
|---|---|
| `ImmutableArray<T>` | Value type; `IsDefault` (uninitialized) round-trips as a null/absent token |
| `ImmutableList<T>` | |
| `ImmutableHashSet<T>` | Deserialized with the default equality comparer |
| `ImmutableSortedSet<T>` | Deserialized with the default comparer |
| `ImmutableDictionary<K,V>` | Deserialized with the default key equality comparer |
| `ImmutableSortedDictionary<K,V>` | Deserialized with the default key comparer |
| `ImmutableStack<T>` | LIFO order round-trips correctly (see note below) |
| `ImmutableQueue<T>` | FIFO order |
| `List<T>` | Mutable — see DeepCopy/Clone note below |
| `Dictionary<K,V>` | Mutable |
| `T[]` (single-dimensional) | Mutable; `byte[]` is handled separately (see note below), not by this generic path |

The element, key, and value types must themselves be serializable (primitives, `Guid`, `DateTimeOffset`, enum, or another `[GenerateSerializer]` type). Using an unsupported element type produces diagnostic **QRK0054** (Error) at code-generation time. This includes nesting one collection inside another (e.g. `ImmutableList<ImmutableList<int>>`, `List<List<int>>`, `Dictionary<string, int[]>`) — collection-of-collection members aren't supported and are rejected via QRK0054 rather than emitting broken generated code. Multi-dimensional or non-zero-based arrays (`int[,]`) are also not recognized.

**Comparer note:** sets and dictionaries deserialized with the default comparer. If the original collection was built with a custom comparer (e.g., `StringComparer.OrdinalIgnoreCase`), that comparer is lost on the round-trip.

**`ImmutableStack<T>` ordering:** `ImmutableStack<T>` enumerates top-to-bottom (LIFO) and has no efficient way to reconstruct itself from an enumerable in that same order via sequential pushes — doing so would reverse the stack. The generated code buffers elements in wire order on read, then pushes back-to-front, so the reconstructed stack has the same top as the original.

**`byte[]` note:** a `byte[]` DTO member is *not* routed through the generic array path above — it keeps using the existing dedicated `ByteArrayCodec` / `GrainMessageSerializer.ByteArray` blob encoding, which is far more efficient than writing one field header per byte.

**DeepCopy / CloneStatic:**
- Immutable collections (`ImmutableArray`/`List`/`HashSet`/`SortedSet`/`Dictionary`/`SortedDictionary`/`Stack`/`Queue`) are shared by reference — no per-element copy is needed because the collection itself cannot be mutated.
- Mutable collections (`List<T>`, `Dictionary<K,V>`, `T[]`) get a **new container** on copy (`new List<T>(input)`, `new Dictionary<K,V>(input)`, `(T[])input.Clone()`) so the copy and the original don't alias the same mutable instance — but element references themselves are still shared. This matches the convention `GrainProxyGenerator` already uses for top-level grain-call arguments (`CloneKind.NewList`/`NewArray`/`NewDictionary`).

## AOT constraints

- Never use `Type.GetType(string)` or runtime `MakeGenericType` in codecs.
- The generated codecs satisfy `IsTrimmable=true` and `EnableAotAnalyzer=true` by default.
- If you write a custom codec, annotate any unavoidable dynamic call with `[RequiresUnreferencedCode]`.
