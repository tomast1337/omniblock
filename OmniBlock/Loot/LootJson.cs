using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Loot.Conditions;
using OmniBlock.Registries;

namespace OmniBlock.Loot;

/// <summary>
///     Builds <see cref="LootTable" />s and <see cref="ILootCondition" />s from JSON, mirroring the
///     string-keyed factory map blocks use in <c>Blocks/Behaviors/BehaviorRegistry.cs</c>.
/// </summary>
internal static class LootJson
{
    private static readonly Dictionary<string, Func<JsonElement, ILootCondition>> s_conditions = new()
    {
        ["killed_by"] = json => new KilledByCondition(json.GetProperty("entity").GetString()!),
        ["on_fire"] = json => new OnFireCondition(!json.TryGetProperty("expected", out var e) || e.GetBoolean()),
        ["sheep_not_sheared"] = _ => new SheepNotShearedCondition(),
        ["size"] = json => new SizeCondition(json.GetProperty("size").GetInt32())
    };

    public static LootTable ParseTable(JsonElement json, IItemRuntimeView items)
    {
        if (!json.TryGetProperty("Pools", out var pools))
        {
            throw new ArgumentException("Loot behavior is missing its 'Pools' array.");
        }

        return new LootTable(pools.EnumerateArray().Select(pool => ParsePool(pool, items)).ToArray());
    }

    private static LootPool ParsePool(JsonElement json, IItemRuntimeView items)
    {
        var entries = json.GetProperty("Entries").EnumerateArray().Select(entry => ParseEntry(entry, items)).ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("Loot pool declares no entries.");
        }

        var min = json.TryGetProperty("MinCount", out var minCount) ? minCount.GetInt32() : 1;
        var max = json.TryGetProperty("MaxCount", out var maxCount) ? maxCount.GetInt32() : -1;
        var condition = json.TryGetProperty("Condition", out var c) ? ParseCondition(c) : null;

        return new LootPool(entries, min, max, condition);
    }

    private static LootEntry ParseEntry(JsonElement json, IItemRuntimeView items)
    {
        var itemName = json.GetProperty("Item").GetString()
                       ?? throw new ArgumentException("Loot entry has a null 'Item'.");

        var weight = json.TryGetProperty("Weight", out var w) ? w.GetInt32() : 1;
        var literalMeta = json.TryGetProperty("Meta", out var m) ? m.GetInt32() : 0;
        var randomVariants = json.TryGetProperty("RandomVariants", out var rv) ? rv.GetInt32() : 0;
        var metaSource = json.TryGetProperty("MetaFrom", out var mf)
            ? Enum.Parse<LootMetaSource>(mf.GetString() ?? nameof(LootMetaSource.Literal), true)
            : LootMetaSource.Literal;

        // Resolved lazily: entity definitions load before blocks' item bridging is complete, and an
        // entry may name a block-derived item id that does not exist yet at parse time.
        var resolved = items.Get(ResourceLocation.Parse(itemName));

        return new LootEntry(
            context =>
            {
                var item = randomVariants > 1
                    ? items.GetByProtocolId(resolved.Id + context.Random.Next(randomVariants))
                    : resolved;
                return new ItemStack(item, 1, metaSource.Resolve(context, literalMeta));
            },
            weight);
    }

    public static ILootCondition ParseCondition(JsonElement json)
    {
        var type = json.GetProperty("type").GetString()
                   ?? throw new ArgumentException("Loot condition is missing its 'type'.");

        return s_conditions.TryGetValue(type, out var factory)
            ? factory(json)
            : throw new ArgumentException($"Unknown loot condition type '{type}'.");
    }
}
