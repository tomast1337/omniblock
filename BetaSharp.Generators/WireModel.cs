namespace BetaSharp.Generators;

/// <summary>The three snippets a field contributes, already specialised to its property.</summary>
internal readonly record struct FieldModel(string Name, string Read, string Write, string Size);

/// <summary>Everything the emitter needs about one <c>[WireMessage]</c> type.</summary>
internal sealed record MessageModel(
    string Namespace,
    string TypeName,
    string Accessibility,
    string KeyNamespace,
    string KeyPath,
    int Version,
    EquatableArray<FieldModel> Fields)
{
    /// <summary>Namespace-qualified, for the generated registration list and the hint name.</summary>
    public string FullName => $"{Namespace}.{TypeName}";
}
