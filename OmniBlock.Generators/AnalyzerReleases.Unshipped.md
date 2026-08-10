; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
OMNI001 | OmniBlock.Network | Error | Message type must be partial
OMNI002 | OmniBlock.Network | Error | Message type must not be nested
OMNI003 | OmniBlock.Network | Error | Message type must derive from Message
OMNI004 | OmniBlock.Network | Error | Message key must be namespace:path
OMNI005 | OmniBlock.Network | Error | Wire field must be a settable instance property
OMNI006 | OmniBlock.Network | Error | Wire field type has no encoding
