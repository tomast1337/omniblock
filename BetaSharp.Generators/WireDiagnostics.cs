using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace BetaSharp.Generators;

/// <summary>
///     A diagnostic reduced to values, so it can travel through the incremental pipeline.
///     <para>
///         <see cref="Diagnostic" /> holds a <see cref="Location" />, which holds a syntax tree,
///         which pins the whole compilation and compares by reference. Carrying one in a cached
///         model leaks memory between compilations and defeats the caching besides.
///     </para>
/// </summary>
internal readonly record struct DiagnosticInfo(
    DiagnosticDescriptor Descriptor, string? FilePath, TextSpanInfo Span, EquatableArray<string> Arguments)
    : IEquatable<DiagnosticInfo>
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, ISymbol symbol, params string[] arguments)
    {
        Location? location = symbol.Locations.FirstOrDefault(l => l.IsInSource);

        return new DiagnosticInfo(
            descriptor,
            location?.SourceTree?.FilePath,
            location is null
                ? default
                : new TextSpanInfo(location.SourceSpan.Start, location.SourceSpan.Length, location.GetLineSpan().Span),
            new EquatableArray<string>(ImmutableArray.Create(arguments)));
    }

    public Diagnostic ToDiagnostic()
    {
        Location? location = FilePath is null
            ? null
            : Location.Create(
                FilePath,
                new Microsoft.CodeAnalysis.Text.TextSpan(Span.Start, Span.Length),
                Span.LineSpan);

        object[] arguments = new object[Arguments.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            arguments[i] = Arguments[i];
        }

        return Diagnostic.Create(Descriptor, location, arguments);
    }
}

/// <summary>The pieces of a source location that compare by value.</summary>
internal readonly record struct TextSpanInfo(
    int Start, int Length, Microsoft.CodeAnalysis.Text.LinePositionSpan LineSpan);

/// <summary>
///     What the generator refuses to guess about.
///     <para>
///         Every one of these is a case where emitting something plausible would produce a message
///         that serialises but does not round-trip — the failure mode that shows up as a corrupt
///         stream on a remote machine rather than as a build error. A build error is the cheaper
///         place to find out.
///     </para>
/// </summary>
internal static class WireDiagnostics
{
    private const string Category = "BetaSharp.Network";

    public static readonly DiagnosticDescriptor MustBePartial = new(
        "OMNI001",
        "Message type must be partial",
        "'{0}' is marked [WireMessage] but is not partial, so the generated serialization cannot be added to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustBeTopLevel = new(
        "OMNI002",
        "Message type must not be nested",
        "'{0}' is marked [WireMessage] but is a nested type; declare message types at namespace level",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MustDeriveFromMessage = new(
        "OMNI003",
        "Message type must derive from Message",
        "'{0}' is marked [WireMessage] but does not derive from Message",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MalformedKey = new(
        "OMNI004",
        "Message key must be namespace:path",
        "'{0}' is not a valid message key; it must be of the form 'namespace:path'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FieldNeedsSetter = new(
        "OMNI005",
        "Wire field must be a settable instance property",
        "'{0}' is marked [WireField] but is static or has no setter, so Read cannot assign it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedFieldType = new(
        "OMNI006",
        "Wire field type has no encoding",
        "'{0}' has no wire encoding, so field '{1}' cannot be generated; write Read/Write/Size by hand for this message",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
