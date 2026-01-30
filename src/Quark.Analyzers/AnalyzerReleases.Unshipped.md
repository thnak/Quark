; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md


### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
QUARK001 | Quark.Streams | Warning | Invalid stream namespace format
QUARK002 | Quark.Streams | Error | Missing IStreamConsumer interface
QUARK003 | Quark.Streams | Warning | Duplicate stream subscription
QUARK004 | Quark.Actors | Warning | Actor method should be async
QUARK005 | Quark.Actors | Warning | Actor class missing [Actor] attribute
QUARK006 | Quark.Actors | Warning | Actor method parameter may not be serializable
QUARK007 | Quark.Actors | Warning | Potential reentrancy issue detected
QUARK008 | Quark.Performance | Warning | Blocking call detected in actor method
QUARK009 | Quark.Performance | Warning | Synchronous I/O detected in actor method
