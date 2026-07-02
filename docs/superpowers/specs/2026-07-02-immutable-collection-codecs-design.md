**Issue:** #108 — serialization support for System.Collections.Immutable  
**Date:** 2026-07-02  
**Status:** Planned — design also posted as a comment on the issue

# Design: Serialization support for `System.Collections.Immutable` types (#108)

## Current state (corrected)

The issue's premise is **wrong on two counts**. There are no immutable-collection codecs — but there are also **no `List<T>`, `Dictionary<K,V>`, or general array codecs**. The only collection type Quark can serialize today is `byte[]`.

**What actually exists** (`src/Quark.Serialization/Codecs/`, registered in `AddPrimitiveCodecs`, `SerializationServiceCollectionExtensions.cs:96-161`): 19 codecs — the integral/floating primitives, `bool`, `char`, `string`, `Guid`, `DateTime`, `DateTimeOffset`, `TimeSpan`, `decimal`, and `byte[]` (`ByteArrayCodec.cs`). None is generic; none handles a sequence or a map. There is no `List<T>` codec, no array codec, no `Dictionary` codec.

**How collection-typed members are handled today — they aren't.** All type-specific serialization logic lives in one place: `SerializerGenerator.GetMemberSerializeInfo` (`SerializerGenerator.cs:126-190`), which maps a member type to a `MemberSerializeKind`. It recognizes only: special-type primitives, `Guid`, `DateTimeOffset`, `[GenerateSerializer]` types (→ `GeneratedCodec`), and enums. **Everything else falls to `MemberSerializeKind.Fallback`.** A `List<T>`, `ImmutableList<T>`, or `T[]` member is `Fallback`, and each of the two serialization paths then fails:

1. **Field-codec path** — the generated `{Type}Codec.WriteField`/`ReadValue` (`SerializerGenerator.cs:239-299`) resolves each member via `_codecs.GetRequiredCodec<TMember>()`. For a collection member no `IFieldCodec<List<T>>` is registered → `CodecNotFoundException` (`CodecProvider.GetRequiredCodec`, `CodecProvider.cs:31-35`). This path is used by **persistence** (`QuarkSerializer`) and **streaming** (`AddStreamableCodec`).
2. **Static positional path** — the generated `{Type}Copier.WriteStatic`/`ReadStatic` (`SerializerGenerator.cs:394-424`) emits `Fallback` members as `GrainMessageSerializer.WriteValue(...)` / `ReadArg(...)` (`EmitMemberWrite`/`EmitMemberRead`, lines 462 & 498). `GrainMessageSerializer.WriteValue` (`GrainMessageSerializer.cs:102-177`) is a boxed `switch` over ~14 `ValueKind`s and **throws `NotSupportedException` for any collection except `byte[]`**. This path is the **transport** path (grain arguments/results).

Net: a `[GenerateSerializer]` DTO with a `List<T>` or any immutable-collection member compiles but **fails at runtime** on both persistence/streaming and transport.

**Two more relevant facts:**
- `ImmutableCopier<T>` (`src/Quark.Serialization/Copiers/ImmutableCopier.cs`) already exists but is a **misnomer** — it is the identity/no-op `IDeepCopier<T>` used for primitives (`DeepCopy` returns the argument unchanged). It is unrelated to immutable collections, but it is exactly the correct copier for them (immutable collections are safe to share by reference), so we reuse it.
- The generator skips generic **container** types (`SerializerGenerator.cs:54-58`), i.e. you cannot put `[GenerateSerializer]` on `Foo<T>`. This does **not** block generic **member** types; a `List<int>` member is fine syntactically — it just routes to `Fallback`.

## Goals / Non-goals

**Goals**
- Serialize/deserialize/deep-copy these as members of `[GenerateSerializer]` types on **both** paths (field-codec and static/transport): `ImmutableArray<T>`, `ImmutableList<T>`, `ImmutableDictionary<K,V>`, `ImmutableHashSet<T>`, `ImmutableSortedDictionary<K,V>`, `ImmutableSortedSet<T>`.
- Element/key/value types may be any type the generator already understands (primitives, `Guid`, `DateTimeOffset`, enum, nested `[GenerateSerializer]`), and — as a stretch within the same emitter — nested supported collections.
- Correctly handle `default(ImmutableArray<T>)` (`IsDefault`) distinctly from an empty array.
- Fully AOT/trim-clean: no reflection, no dynamic code, no new trim warnings.
- Emit a clear diagnostic for an unsupported element type rather than silently producing throwing code.

**Non-goals**
- Custom `IEqualityComparer`/`IComparer` preservation for `ImmutableHashSet`/`ImmutableDictionary`/sorted variants. Deserialization uses the **default** comparer (matches Orleans). Documented limitation.
- `List<T>`/`Dictionary<K,V>`/array codecs. Out of scope for #108, but this design **defines the canonical collection wire format** so a future mutable-collection issue slots in and interoperates (see Wire-format compatibility).
- Immutable collections as **top-level grain-call arguments** without an enclosing `[GenerateSerializer]` type. The static path for bare args goes through `GrainMessageSerializer`'s boxed switch; extending that is a separate concern. In practice immutable collections are used inside state/DTO types, which is covered.
- `ImmutableStack<T>`/`ImmutableQueue<T>` (rare; can follow the same pattern later).

## Proposed design

**Recommendation: generator special-casing in `SerializerGenerator`, not standalone generic codecs.** Justification:

- Standalone generic codecs (`IFieldCodec<ImmutableList<T>>` + manual `AddCodec<ImmutableList<int>, …>()`) can only serve the **field-codec** path. The **static/transport** path (`GrainMessageSerializer`) is a boxed type switch that cannot dispatch to open-generic codecs and would still throw. So codecs alone cannot make a state DTO round-trip over transport.
- The generator is already the **single** place that special-cases types (enum, `Guid`, `DateTimeOffset`, `[GenerateSerializer]`). Immutable collections belong in the same dispatch.
- No per-instantiation manual registration; concrete generic instantiations (`ImmutableArray.CreateBuilder<int>()`) are emitted directly, which is what keeps it AOT-safe.
- There is no existing `List<T>` mechanism to "mirror," so the framing in the issue ("analogous to the existing `List<T>` codecs") does not apply — we set the pattern.

**Generator changes (all in `src/Quark.CodeGenerator/SerializerGenerator.cs`):**

1. **Recursive type-info resolution.** Refactor `GetMemberSerializeInfo` into a reusable `ResolveSerializeInfo(ITypeSymbol)` returning a small record that can be *nested* (a collection info carries its element/key/value `SerializeInfo`). Add a `CollectionShape` enum: `ImmutableArray, ImmutableList, ImmutableHashSet, ImmutableSortedSet, ImmutableDictionary, ImmutableSortedDictionary`. Recognize by original-definition fully-qualified name (`System.Collections.Immutable.ImmutableList<T>`, etc.) via `INamedTypeSymbol.ConstructedFrom`/`OriginalDefinition`.

2. **Recursive write/read emitters** shared by member-level and element-level emission, one per path:
   - *Field-codec path* (inside `{Type}Codec`): emit the collection under one field header as a nested group. Write `VarUInt32` count, then each element via the element's field codec (`GetRequiredCodec<TElem>()`) using a synthetic per-element field id. Read mirrors it. Null/`IsDefault` encoded with the existing `WireType.Extended`+`ExtendedWireType.Null` convention already used by `ByteArrayCodec`/reference members.
   - *Static positional path* (inside `{Type}Copier.WriteStatic`/`ReadStatic`): emit a presence/length token then positional elements via the recursive static emitter (reusing the primitive `writer.WriteInt32`/`WriteString`/… forms already in `EmitMemberWrite`, and `{Elem}Copier.WriteStatic` for nested `[GenerateSerializer]`). This is the path where builders are used on read.

3. **Builder-based read loops** (static path shown; field path analogous):
   - `ImmutableArray<T>`: write — if `value.IsDefault` emit null token, else `count` then elements. Read — `var b = ImmutableArray.CreateBuilder<T>(count); for(...) b.Add(elem); return b.MoveToImmutable();` (`CreateBuilder(count)` + `MoveToImmutable` avoids a reallocation; exact-capacity contract satisfied).
   - `ImmutableList<T>`: `var b = ImmutableList.CreateBuilder<T>(); … b.Add(elem); return b.ToImmutable();`
   - `ImmutableHashSet<T>`: `ImmutableHashSet.CreateBuilder<T>()` + `Add` + `ToImmutable()`.
   - `ImmutableSortedSet<T>`: `ImmutableSortedSet.CreateBuilder<T>()` + `Add` + `ToImmutable()`.
   - `ImmutableDictionary<K,V>`: `ImmutableDictionary.CreateBuilder<K,V>()` + `b[k]=v` + `ToImmutable()`; wire = `count` then `(key,value)` pairs.
   - `ImmutableSortedDictionary<K,V>`: `ImmutableSortedDictionary.CreateBuilder<K,V>()` + `ToImmutable()`.

4. **DeepCopy / CloneStatic.** In `{Type}Copier.DeepCopy` (`SerializerGenerator.cs:343-366`), collection members are deep-copied by **reference** (immutable → identity copy); emit `copy.X = input.X;` directly instead of `GetRequiredCopier<…>()`. `CloneStatic` already shallow-copies members by reference, which is correct for immutable collections — no change needed there.

5. **Diagnostic.** New `QRK0012` (id subject to the analyzer numbering owner) when a collection element/key/value type is not serializable by the generator, reported at the member. Prevents emitting code that would `CodecNotFound` at runtime.

**No new runtime types are strictly required.** Optionally, for the *field-codec* path we could instead register hand-written open-generic codecs, but per the recommendation above we keep all emission in the generator to serve both paths uniformly. `ImmutableCopier<T>` is reused conceptually (identity copy) but emitted inline, so no DI registration is added.

**Compatibility tier: drop-in.** User code is identical to Orleans — annotate a member `[Id(n)] public ImmutableList<T> X { get; set; }` and it works with no Quark-specific attribute or registration. Behavioral parity with Orleans' immutable-collection support (dotnet/orleans#1464), including the default-comparer-on-read limitation.

## Wire-format compatibility

- **Canonical sequence format** (both a future `List<T>`/`T[]` and `ImmutableList<T>`/`ImmutableArray<T>` use it): `[presence/null][VarUInt32 count][element…]×count`. **Map format**: `[presence/null][VarUInt32 count][key,value]×count`. Because the format is defined by element *sequence*, not the concrete collection type, `ImmutableArray<T> ↔ T[]` and `ImmutableList<T> ↔ List<T>` are the same equivalence class per path.
- **Guarantee (forward):** the intent is that a member serialized as `List<T>` in v1 and `ImmutableList<T>` in v2 round-trips. Since mutable-collection support does **not exist yet**, this is a *forward* guarantee: this design fixes the canonical format now, and a future mutable-collection implementation MUST adopt the same shared emitter to inherit it. Within the immutable set delivered here, `ImmutableArray<T> ↔ ImmutableList<T>` (both sequence-shaped) and `ImmutableDictionary ↔ ImmutableSortedDictionary` (both map-shaped) round-trip; a sorted variant read from an unsorted-written payload simply re-sorts under the default comparer.
- **Two paths, two formats — by design.** The field-codec path is field-header/tag-delimited; the static path is positional. These already differ for every existing type. Collection wire-compat is asserted **within each path**, not across them (consistent with all current codecs). A value written by the persistence path is read by the persistence path; transport by transport.
- **`ImmutableArray<T>` default vs empty:** `IsDefault` → null token (reads back to `default(ImmutableArray<T>)`); `IsEmpty` → `count = 0` (reads back to `ImmutableArray<T>.Empty`). These are distinct on the wire.
- **Sets/dictionaries:** contents preserved, not the original comparer. Enumeration order is written as-is; sorted variants re-order on read.

## AOT notes

- **No reflection, no dynamic code.** Every builder call (`ImmutableArray.CreateBuilder<int>()`, `ImmutableDictionary.CreateBuilder<string, Foo>()`, …) is a concrete generic instantiation emitted into generated C#, statically reachable by the AOT compiler — the same reachability model as the existing `GetRequiredCodec<int>()` calls.
- **No new trim warnings.** `System.Collections.Immutable` builder/`ToImmutable`/`MoveToImmutable`/`Add` APIs carry no `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` annotations; they operate on the already-instantiated element type. Nothing here triggers `EnableAotAnalyzer`. No `ISerializable`, so no QRK0003.
- **No `IGeneralizedCodec` runtime type-dispatch** is introduced (which would be the reflective route). Immutable members are resolved entirely at generate-time by symbol inspection.
- Verify with the existing Native AOT smoke publish of `Quark.Runtime` plus a sample DTO carrying immutable members.

## Test plan

Add to `tests/Quark.Tests.CodeGenerator/SerializerGeneratorTests.cs` and a runtime round-trip suite in `tests/Quark.Tests.Unit` (serialization):

- **Generator emission** — for a DTO with one member of each of the six collection types, assert the generator produces no diagnostics and the expected builder calls appear (`CreateBuilder`, `MoveToImmutable`/`ToImmutable`).
- **Field-codec round-trip** (persistence/streaming path) via `QuarkSerializer`: populate, serialize, deserialize, assert element-wise equality for each of the six types.
- **Static/transport round-trip** via `{Type}Copier.WriteStatic`/`ReadStatic`: same six types inside a DTO used as a grain-call argument.
- **`ImmutableArray<T>` edge cases:** `default` (IsDefault) round-trips to `IsDefault`; `Empty` round-trips to `IsEmpty`; populated round-trips element-order-preserving.
- **Nested element types:** `ImmutableList<Foo>` where `Foo` is `[GenerateSerializer]`; `ImmutableDictionary<string, Foo>`; `ImmutableList<MyEnum>`; `ImmutableArray<Guid>`.
- **Null reference collections:** `ImmutableList<T>? = null` round-trips to null on both paths.
- **Cross-shape compat (within a path):** write `ImmutableArray<int>`, read into an `ImmutableList<int>` member of a structurally-matching DTO → equal contents (documents the sequence-format equivalence).
- **Diagnostic:** a DTO with `ImmutableList<SomeUnsupportedType>` produces `QRK0012` and does not emit throwing code.
- **DeepCopy:** `IDeepCopier` deep-copy of a DTO returns the same immutable-collection references (identity).
- **AOT:** extend the Native AOT smoke build with a DTO carrying immutable members; confirm zero trim/AOT warnings.
- **End-to-end:** a `Quark.Tests.Integration` grain whose `[PersistentState]`/`IActivationMemory` state holds an `ImmutableList<T>`, exercised over the TCP path.

## Implementation checklist

- [ ] `SerializerGenerator.cs`: introduce `CollectionShape` enum and a nested `SerializeInfo` record capable of carrying element/key/value info.
- [ ] `SerializerGenerator.cs`: refactor `GetMemberSerializeInfo` → recursive `ResolveSerializeInfo(ITypeSymbol)`; recognize the six immutable types by `OriginalDefinition` FQN.
- [ ] `SerializerGenerator.cs`: recursive field-codec emitters (write/read) for collections inside `{Type}Codec`, with count-prefix + per-element field codec + null/`IsDefault` handling.
- [ ] `SerializerGenerator.cs`: recursive static-path emitters (`WriteStatic`/`ReadStatic`) for collections, with builder-based read loops per shape and presence/length token.
- [ ] `SerializerGenerator.cs`: `DeepCopy` emits reference (identity) copy for immutable-collection members; confirm `CloneStatic` unchanged.
- [ ] `SerializerGenerator.cs`: add `QRK0012` diagnostic for unsupported element/key/value types; wire into the pipeline.
- [ ] Register `QRK0012` descriptor alongside existing generator diagnostics (`QRK0010`/`QRK0011`).
- [ ] `SerializerGeneratorTests.cs`: emission + diagnostic tests for all six types.
- [ ] `Quark.Tests.Unit`: field-codec and static-path round-trip tests, incl. `ImmutableArray` default/empty edge cases, nested `[GenerateSerializer]`/enum/`Guid` elements, null, cross-shape compat.
- [ ] `Quark.Tests.Integration`: end-to-end grain state with an immutable collection over TCP.
- [ ] Extend Native AOT smoke build with an immutable-member DTO; confirm no warnings.
- [ ] Docs: `wiki/Serialization.md` — add "Immutable collections" section (supported set, default-comparer limitation, `ImmutableArray` default-vs-empty semantics).
- [ ] `FEATURES.md`: mark immutable-collection serialization; note `List`/`Dictionary`/array remain unimplemented (correct the record vs #108's premise).

## Resolved design decisions

1. **Mechanism:** generator special-casing, not standalone generic codecs — only the generator can serve both the field-codec and static/transport paths; codecs serve only the former.
2. **Scope truth:** neither immutable nor mutable collection serialization exists today; the issue's "List/Dictionary/array codecs exist" premise is false. We implement immutable now and define the canonical format for a later mutable follow-up.
3. **Both paths in scope:** field-codec (persistence/streaming) *and* static (transport). Fixing only one leaves state DTOs broken end-to-end.
4. **Copier:** immutable collections deep-copy by reference (identity); reuse the `ImmutableCopier<T>` semantics, emitted inline. No new DI registration.
5. **`ImmutableArray<T>` (struct):** `IsDefault` serialized as null token, distinct from `IsEmpty` (`count=0`).
6. **Comparers not preserved:** sets/dictionaries deserialize with the default comparer (Orleans parity). Documented limitation, not a bug.
7. **Wire-compat scope:** guaranteed within a path and within a shape-class; `List↔ImmutableList` interop is a forward guarantee contingent on a future mutable implementation adopting the shared emitter.
8. **Unsupported elements fail loud:** `QRK0012` at generate-time rather than a runtime throw.
9. **Compatibility tier:** drop-in (user code identical to Orleans; no Quark-specific surface).

## Open questions

- Confirm the diagnostic id `QRK0012` is free (next after `QRK0011`) and owned by the generator, not the analyzer range.
- Should `ImmutableStack<T>`/`ImmutableQueue<T>` be folded into this issue or deferred? (Recommend defer.)
- Is a follow-up mutable-collection issue (`List`/`Dictionary`/`T[]`) desired now so the shared emitter is designed for reuse from day one, rather than retrofitted?
