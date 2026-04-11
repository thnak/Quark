# Native AOT and Trimming Guide

This document is the dedicated location for Native AOT and linker-trimming guidance referenced from the repository architecture guide.

At minimum, contributors should follow these rules when adding new runtime features:

1. Prefer source generation over runtime reflection.
2. Annotate unavoidable dynamic behavior with `[RequiresUnreferencedCode]` and/or `[RequiresDynamicCode]`.
3. Guard JIT-only code paths with `RuntimeFeature.IsDynamicCodeSupported`.
4. Prefer `[UnsafeAccessor]` over `DynamicMethod` for private member access on .NET 8+.
5. Avoid introducing new `ISerializable`-based patterns.
6. Prefer explicit provider registration over trim-unsafe assembly scanning.
