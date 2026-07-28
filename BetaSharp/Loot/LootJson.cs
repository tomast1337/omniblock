using System.Text.Json;
using BetaSharp.Items;
using BetaSharp.Loot.Conditions;

namespace BetaSharp.Loot;

/// <summary>
///     Builds <see cref="LootTable" />s and <see cref="ILootCondition" />s from JSON, mirroring the
///     string-keyed factory map blocks use in <c>Blocks/Behaviors/BehaviorRegistry.cs</c>.
/// </summary>
internal static class LootJson
{
    private static readonly Dictionary<string, Func<JsonElement, ILootCondition>> s_conditions = new()
    {
        ["killed_by"] = json => new KilledByCondition(json.GetProperty("entity").GetString()!),
        ["on_fire"] = json => new OnFireCondition(!json.TryGetProperty("expected", out JsonElement e) || e.GetBoolean()),
        ["sheep_not_sheared"] = _ => new SheepNotShearedCondition(),
        ["size"] = json => new SizeCondition(json.GetProperty("size").GetInt32())
    };

    public static LootTable ParseTable(JsonElement json)
    {
        if (!json.TryGetProperty("Pools", out JsonElement pools))
        {
            throw new ArgumentException("Loot behavior is missing its 'Pools' array.");
        }

        return new LootTable(pools.EnumerateArray().Select(ParsePool).ToArray());
    }

    private static LootPool ParsePool(JsonElement json)
    {
        LootEntry[] entries = json.GetProperty("Entries").EnumerateArray().Select(ParseEntry).ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("Loot pool declares no entries.");
        }

        int min = json.TryGetProperty("MinCount", out JsonElement minCount) ? minCount.GetInt32() : 1;
        int max = json.TryGetProperty("MaxCount", out JsonElement maxCount) ? maxCount.GetInt32() : -1;
        ILootCondition? condition = json.TryGetProperty("Condition", out JsonElement c) ? ParseCondition(c) : null;

        return new LootPool(entries, min, max, condition);
    }

    private static LootEntry ParseEntry(JsonElement json)
    {
        string itemName = json.GetProperty("Item").GetString()
            ?? throw new ArgumentException("Loot entry has a null 'Item'.");

        int weight = json.TryGetProperty("Weight", out JsonElement w) ? w.GetInt32() : 1;
        int literalMeta = json.TryGetProperty("Meta", out JsonElement m) ? m.GetInt32() : 0;
        int randomVariants = json.TryGetProperty("RandomVariants", out JsonElement rv) ? rv.GetInt32() : 0;
        LootMetaSource metaSource = json.TryGetProperty("MetaFrom", out JsonElement mf)
            ? Enum.Parse<LootMetaSource>(mf.GetString() ?? nameof(LootMetaSource.Literal), true)
            : LootMetaSource.Literal;

        // Resolved lazily: entity definitions load before blocks' item bridging is complete, and an
        // entry may name a block-derived item id that does not exist yet at parse time.
        int? cached = null;
        int ResolveId() => cached ??= ItemLookup.TryGetItemId(itemName, out int id)
            ? id
            : throw new ArgumentException($"Unknown item or block in loot entry: '{itemName}'.");

        return new LootEntry(
            context =>
            {
                int id = ResolveId();
                if (randomVariants > 1) id += context.Random.Next(randomVariants);
                return new ItemStack(id, 1, metaSource.Resolve(context, literalMeta));
            },
            weight);
    }

    public static ILootCondition ParseCondition(JsonElement json)
    {
        string type = json.GetProperty("type").GetString()
            ?? throw new ArgumentException("Loot condition is missing its 'type'.");

        return s_conditions.TryGetValue(type, out Func<JsonElement, ILootCondition>? factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown loot condition type '{type}'.");
    }
}
