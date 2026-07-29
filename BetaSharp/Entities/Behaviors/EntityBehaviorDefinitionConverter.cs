using System.Text.Json;
using System.Text.Json.Serialization;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Reads a behavior entry's <c>"Type"</c> key and deserializes the rest of the object into the
///     matching <see cref="EntityBehaviorDefinition" /> subclass.
///     <para>
///         Hand-written rather than <c>[JsonPolymorphic]</c> for two reasons: the discriminator can
///         appear anywhere in the object rather than having to come first, and an unknown type name
///         produces a message naming it instead of a generic deserialization failure.
///     </para>
/// </summary>
internal sealed class EntityBehaviorDefinitionConverter : JsonConverter<EntityBehaviorDefinition>
{
    /// <summary>
    ///     Behavior parameters are snake_case on disk while the surrounding entity definition is
    ///     PascalCase, so this converter deserializes with its own options.
    /// </summary>
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    public override EntityBehaviorDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement json = document.RootElement;

        if (!json.TryGetProperty("Type", out JsonElement typeJson) || typeJson.GetString() is not { } typeName)
        {
            throw new JsonException("Behavior entry is missing its 'Type' key.");
        }

        if (EntityBehaviorDefinitionRegistry.Resolve(typeName) is not { } clrType)
        {
            // Not converted to a typed definition yet — fall back to the string-keyed factory.
            return new LegacyBehaviorDefinition(typeName, json.Clone())
            {
                Slots = ReadSlots(json, typeName)
            };
        }

        return (EntityBehaviorDefinition)(json.Deserialize(clrType, s_options)
            ?? throw new JsonException($"Behavior '{typeName}' deserialized to null."));
    }

    private static string[] ReadSlots(JsonElement json, string typeName)
    {
        if (!json.TryGetProperty("Slots", out JsonElement slots))
        {
            throw new JsonException($"Behavior '{typeName}' is missing its 'Slots' array.");
        }

        return [.. slots.EnumerateArray().Select(slot => slot.GetString()
            ?? throw new JsonException($"Behavior '{typeName}' has a null entry in 'Slots'."))];
    }

    public override void Write(Utf8JsonWriter writer, EntityBehaviorDefinition value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Entity behavior definitions are read-only data.");
}
