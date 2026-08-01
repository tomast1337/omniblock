; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
OMNI001 | BetaSharp.Network | Error | Message type must be partial
OMNI002 | BetaSharp.Network | Error | Message type must not be nested
OMNI003 | BetaSharp.Network | Error | Message type must derive from Message
OMNI004 | BetaSharp.Network | Error | Message key must be namespace:path
OMNI005 | BetaSharp.Network | Error | Wire field must be a settable instance property
OMNI006 | BetaSharp.Network | Error | Wire field type has no encoding
