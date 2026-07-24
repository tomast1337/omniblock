using BetaSharp.Items;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Blocks;

public static class BlockRegistry
{
    // Keyed by BlockDefinition.Name (unique, the JSON filename) — not Block.RegistryName /
    // TranslationKey, which several blocks legitimately share (Repeater/PoweredRepeater both
    // translate as "diode", Snow/SnowBlock both as "snow", ~9 such pairs total). A lookup keyed
    // by the non-unique translation key could only ever resolve one of each pair.
    private static readonly CanonicalRegistry<BlockDefinition> s_registry = new("block");

    // id -> unique registry name, so ItemLookup (which needs to key by the SAME unique name,
    // not the collision-prone RegistryName) can build its block-name table without reflecting
    // Block.cs's static fields — those are gone after this migration.
    private static readonly Dictionary<int, string> s_idToName = [];

    /// <summary>Returns the live, constructed <see cref="Block" /> registered under <paramref name="name" />.</summary>
    public static Block Get(string name)
    {
        BlockDefinition definition = s_registry.Get(name);
        return Block.Blocks[definition.ProtocolId]
            ?? throw new InvalidOperationException($"Block '{name}' is registered but LoadAndBuild has not run yet.");
    }

    /// <summary>Reverse lookup used by <see cref="BetaSharp.ItemLookup" /> to build its block-name table.</summary>
    internal static string? TryGetName(int protocolId) => s_idToName.GetValueOrDefault(protocolId);

    internal static void Initialize()
    {
        if (s_registry.IsInitialized) return;

        var loader = (BlockDefinitionJsonLoader)RegistryDefinitions.Blocks.CreateLoader();
        loader.LoadFromPaths(null, null, null);
        if (loader.HasErrors)
        {
            throw new AssetLoadException(loader.FirstErrorMessage ?? "One or more block definitions failed to load.");
        }

        List<BlockDefinition> definitions = loader.ToList();
        s_registry.Initialize(definitions, static d => d);
        foreach (BlockDefinition def in definitions)
        {
            s_idToName[def.ProtocolId] = def.Name;
        }

        LoadAndBuild(definitions);
        BridgeToItems(definitions);

        // Air (id 0, never a registered Block) always allows vision through it — the old static
        // constructor set this once after building every block; nothing else does now.
        Block.BlocksAllowVision[0] = true;
    }

    private static void LoadAndBuild(IEnumerable<BlockDefinition> definitions)
    {
        List<BlockDefinition> defs = definitions.ToList();

        // Pass 1: construct every plain Block. Block's constructor plus the fluent setters
        // BlockFactory.Create calls already populate every hot-path array (Blocks,
        // BlocksOpaque, BlockLightOpacity, BlocksAllowVision, BlocksRandomTick,
        // BlocksIgnoreMetaUpdate, BlocksLightLuminance) — nothing further to do here, and
        // re-deriving them a second time here would just be a second place to drift out of
        // sync with those setters.
        foreach (BlockDefinition def in defs)
        {
            BlockFactory.Create(def);
        }

        // Every Block now exists in Block.Blocks[], so it's safe for ItemLookup to build its
        // name table (which includes a block-name entry per Block.Blocks[] slot) — needed by
        // pass 2 below, since loot tables commonly name another block as their drop (Stone
        // drops "cobblestone") and resolve through ItemLookup, not Item.ByName.
        ItemLookup.Initialize();

        // Pass 2: resolve behaviors, loot tables, and cross-block references now that every
        // Block exists and is reachable by Block.ByName.
        foreach (BlockDefinition def in defs)
        {
            BlockFactory.AttachBehaviors(Block.Blocks[def.ProtocolId], def);
        }
    }

    /// <summary>
    ///     Replaces <c>Block</c>'s old static constructor, which special-cased seven blocks to
    ///     wrap a specific <see cref="Item" /> subclass instead of a generic <see cref="ItemBlock" />
    ///     (those items need subtype-aware behavior — per-metadata name/texture, or in
    ///     ItemPiston's case picking between two block ids — that a plain ItemBlock can't
    ///     express). Keyed by <see cref="BlockDefinition.Name" /> now instead of a hardcoded
    ///     C# field reference.
    /// </summary>
    private static void BridgeToItems(IEnumerable<BlockDefinition> definitions)
    {
        var specialCased = new Dictionary<string, Func<int, Item>>
        {
            ["wool"] = id => new ItemCloth(id - 256).setItemName("cloth"),
            ["log"] = id => new ItemLog(id - 256).setItemName("log"),
            ["slab"] = id => new ItemSlab(id - 256).setItemName("stoneSlab"),
            ["sapling"] = id => new ItemSapling(id - 256).setItemName("sapling"),
            ["leaves"] = id => new ItemLeaves(id - 256).setItemName("leaves"),
            ["piston"] = id => new ItemPiston(id - 256),
            ["sticky_piston"] = id => new ItemPiston(id - 256),
        };

        foreach (BlockDefinition def in definitions)
        {
            int id = def.ProtocolId;
            Item.ITEMS[id] = specialCased.TryGetValue(def.Name, out Func<int, Item>? factory)
                ? factory(id)
                : new ItemBlock(id - 256);

            // Every block gets Init() called once, all definitions guaranteed constructed,
            // simpler than the old code's skip-if-Item.ITEMS-already-set quirk, and provably
            // identical in practice: none of the seven special-cased blocks above override
            // IBlockLifecycle.OnInit (only FireBehavior and LeavesBehavior do, and neither is
            // one of them).
            Block.Blocks[id].Init();
        }
    }
}
