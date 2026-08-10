using System.Diagnostics.CodeAnalysis;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock;

internal static class ItemLookup
{
    private static readonly Dictionary<string, int> s_itemNameToId = [];
    private static readonly Dictionary<string, (int id, int meta)> s_alias = [];
    private static bool s_lookupTablesBuilt;

    public static void Initialize() => BuildItemLookupTables();

    internal static bool TryGetItemId(string input, out int itemId)
    {
        int colon = input.IndexOf(':');
        if (colon < 0) return TryGetItemId(input, out itemId, false);

        string a = input[..colon];
        string b = input[(colon + 1)..];
        if (!int.TryParse(b, out _))
        {
            // Ignore item meta/damage
            TryGetItemId(a, out itemId, false);
        }

        // found namespace
        return TryGetItemId(Namespace.Get(a), b, out itemId);
    }

    // TODO: respect namespace in item ID lookup.
    internal static bool TryGetItemId(Namespace @namespace, string input, out int itemId)
    {
        if (s_alias.TryGetValue(input, out (int id, int meta) alias))
        {
            itemId = alias.id;
            return true;
        }

        return TryGetItemId(input, out itemId, true);
    }

    private static bool TryGetItemId(string input, out int itemId, bool haveNamespace)
    {
        if (int.TryParse(input, out itemId))
        {
            return itemId >= 0 && itemId < Item.Items.Length && Item.Items[itemId] != null;
        }

        return s_itemNameToId.TryGetValue(input.ToLower(), out itemId);
    }

    /// <summary>
    /// Parses "id", "name", "id:damage", or "name:damage" into an ItemStack.
    /// </summary>
    internal static bool TryGetItem(string input, [NotNullWhen(true)] out ItemStack? itemId, int itemCount = 1, int meta = 0) =>
        TryGetItem(input, out itemId, itemCount, meta, false);

    private static bool TryGetItem(string input, [NotNullWhen(true)] out ItemStack? itemId, int itemCount, int meta, bool haveNamespace)
    {
        int colon = input.IndexOf(':');

        string name;
        int damage;

        if (colon < 0)
        {
            name = input;
            damage = meta;
        }
        else
        {
            name = input[..colon];
            string damageStr = input[(colon + 1)..];
            if (!int.TryParse(damageStr, out damage))
            {
                if (haveNamespace)
                {
                    throw new Exception("Invalid item meta. Expected number found \"" + damageStr + "\"");
                }

                // damageStr is the item name.
                // name is the namespace.
                return TryGetItem(Namespace.Get(name), damageStr, out itemId, itemCount, meta);
            }
        }

        if (s_alias.TryGetValue(name, out (int id, int meta) alias))
        {
            itemId = new ItemStack(alias.id, itemCount, alias.meta);
            return true;
        }

        if (!TryGetItemId(name, out int id, haveNamespace))
        {
            itemId = null;
            return false;
        }

        itemId = new ItemStack(id, itemCount, damage);
        return true;
    }

    // TODO: respect namespace in item lookup.
    internal static bool TryGetItem(Namespace @namespace, string input, [NotNullWhen(true)] out ItemStack? itemId, int itemCount = 1, int meta = 0) =>
        TryGetItem(input, out itemId, itemCount, meta, true);

    internal static string ResolveItemName(ItemStack item) =>
        s_itemNameToId.FirstOrDefault(kvp => kvp.Value == item.ItemId).Key ?? item.GetItemName();

    /// <summary>
    /// Gets all available item names that start with the given prefix (with underscores)
    /// </summary>
    public static List<string> GetAvailableItemNames(string prefix = "")
    {
        if (!s_lookupTablesBuilt)
        {
            BuildItemLookupTables();
        }

        return s_itemNameToId.Keys
            .Where(name => string.IsNullOrEmpty(prefix) || name.StartsWith(prefix.ToLower()))
            .OrderBy(name => name)
            .ToList();
    }

    private static void BuildItemLookupTables()
    {
        if (s_lookupTablesBuilt)
        {
            return;
        }

        s_lookupTablesBuilt = true;

        // Standalone items are registered FIRST: a handful of names name both a block and an
        // unrelated standalone item with the same base name (bed, cake, clay, sign, wheat — the
        // block's own drop is that item, e.g. the Clay block drops the Clay item). TryAdd below
        // means whichever loop runs first wins a shared name, and a loot table entry naming one
        // of these always means the holdable item, never the block itself.
        foreach (ItemDefinition definition in DefaultRegistries.Items)
        {
            ResourceLocation? location = DefaultRegistries.Items.GetKey(definition);
            if (location is not null)
            {
                s_itemNameToId.TryAdd(location.Path, definition.ProtocolId);
            }

            if (Item.Items[definition.ProtocolId] is { } item)
            {
                BuildItemLookupAlias(item);
            }
        }

        for (int id = 0; id < Block.Blocks.Length; id++)
        {
            if (Block.Blocks[id] is not { } block) continue;

            if (BlockRegistry.TryGetName(id) is { } name)
            {
                s_itemNameToId.TryAdd(name, id);

                // Recipes and other data predate the JSON migration's snake_case convention and
                // reference the old field-name-derived form (e.g. "brownmushroom", not
                // "brown_mushroom") — collapsing underscores out of the new name reconstructs it
                // exactly, since the old form was always just the field name lowercased with no
                // separators. Registered additively so both forms keep resolving.
                s_itemNameToId.TryAdd(name.Replace("_", ""), id);
            }

            BuildItemLookupAlias(block);
        }
    }

    private static void BuildItemLookupAlias(Item item)
    {
        foreach (string alias in item.GetItemAlias)
        {
            string s = alias.ToLower();
            int i = s.LastIndexOf(':');
            if (i == -1) s_itemNameToId.TryAdd(s, item.Id);
            else
            {
                int meta = int.Parse(s.Substring(i + 1, s.Length - i - 1));
                if (meta == 0) s_itemNameToId.TryAdd(s.Substring(0, i), item.Id);
                else s_alias.TryAdd(s.Substring(0, i), (item.Id, meta));
            }
        }
    }

    private static void BuildItemLookupAlias(Block block)
    {
        foreach (string alias in block.GetBlockAlias)
        {
            string s = alias.ToLower();
            int i = s.LastIndexOf(':');
            if (i == -1) s_itemNameToId.TryAdd(s, block.Id);
            else
            {
                int meta = int.Parse(s.Substring(i + 1, s.Length - i - 1));
                if (meta == 0) s_itemNameToId.TryAdd(s.Substring(0, i), block.Id);
                else s_alias.TryAdd(s.Substring(0, i), (block.Id, meta));
            }
        }
    }
}
