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

Arrays and common collection types (`List<T>`, `Dictionary<K,V>`, etc.) are handled by generic wrapper codecs that delegate to the element codec.

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

## AOT constraints

- Never use `Type.GetType(string)` or runtime `MakeGenericType` in codecs.
- The generated codecs satisfy `IsTrimmable=true` and `EnableAotAnalyzer=true` by default.
- If you write a custom codec, annotate any unavoidable dynamic call with `[RequiresUnreferencedCode]`.
