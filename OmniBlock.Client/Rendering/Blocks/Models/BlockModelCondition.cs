using System.Text.Json;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// A validated, immutable predicate over boolean state inputs captured for a mesh build.
/// The owning block adapter supplies its named inputs; evaluation never reads a live world.
/// </summary>
internal readonly record struct BlockModelCondition(int Required, int Forbidden)
{
    internal bool IsUnconditional => Required == 0 && Forbidden == 0;
    internal bool Matches(int state) => (state & Required) == Required && (state & Forbidden) == 0;

    internal static BlockModelCondition Parse(JsonElement definition,
        IReadOnlyDictionary<string, int> inputs)
    {
        if (definition.ValueKind == JsonValueKind.String)
        {
            var name = definition.GetString()!;
            if (name == "always") return default;
            if (!inputs.TryGetValue(name, out var bit))
                throw new InvalidDataException($"unknown condition '{name}'");
            return new BlockModelCondition(bit, 0);
        }
        if (definition.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("condition must be 'always', a named input, or an input object");
        var required = 0;
        var forbidden = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in definition.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                throw new InvalidDataException($"duplicate condition input '{property.Name}'");
            if (!inputs.TryGetValue(property.Name, out var bit))
                throw new InvalidDataException($"unknown condition input '{property.Name}'");
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException($"condition input '{property.Name}' must be boolean");
            if (property.Value.GetBoolean()) required |= bit;
            else forbidden |= bit;
        }
        if (seen.Count == 0)
            throw new InvalidDataException("empty condition object; use 'always' explicitly");
        if ((required & forbidden) != 0)
            throw new InvalidDataException("condition requires and forbids the same input");
        return new BlockModelCondition(required, forbidden);
    }
}
