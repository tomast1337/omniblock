using BetaSharp.Items;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Blocks;

public static class BlockRegistry
{
    private static readonly CanonicalRegistry<BlockDefinition> s_registry = new("block");

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
        Block.BlocksAllowVision[0] = true;
    }

    private static void LoadAndBuild(IEnumerable<BlockDefinition> definitions)
    {
        List<BlockDefinition> defs = definitions.ToList();

        foreach (BlockDefinition def in defs)
        {
            BlockFactory.Create(def);
        }

        ItemLookup.Initialize();

        foreach (BlockDefinition def in defs)
        {
            BlockFactory.AttachBehaviors(Block.Blocks[def.ProtocolId], def);
        }
    }

    private static void BridgeToItems(IEnumerable<BlockDefinition> definitions)
    {
        var specialCased = new Dictionary<string, Func<int, Item>>
        {
            ["wool"] = id => new ItemCloth(id - 256).SetItemName("cloth"),
            ["log"] = id => new ItemLog(id - 256).SetItemName("log"),
            ["slab"] = id => new ItemSlab(id - 256).SetItemName("stoneSlab"),
            ["sapling"] = id => new ItemSapling(id - 256).SetItemName("sapling"),
            ["grass"] = id => new ItemGrass(id - 256).SetItemName("grass"),
            ["leaves"] = id => new ItemLeaves(id - 256).SetItemName("leaves"),
            ["piston"] = id => new ItemPiston(id - 256),
            ["sticky_piston"] = id => new ItemPiston(id - 256),
        };

        foreach (BlockDefinition def in definitions)
        {
            int id = def.ProtocolId;
            Item.Items[id] = specialCased.TryGetValue(def.Name, out Func<int, Item>? factory)
                ? factory(id)
                : new ItemBlock(id - 256);

            // Every block gets Init() called once, all definitions guaranteed constructed,
            // simpler than the old code's skip-if-Item.ITEMS-already-set quirk, and provably
            // identical in practice: none of the seven special-cased blocks above override
            // IBlockLifecycle.OnInit (no block does, AttachBehaviors builds a separate
            // instance per slot, so instance state set via OnInit wouldn't reliably reach
            // the Ticker/Physics/etc-slot instances anyway; behaviors needing resolved
            // cross-block ids use static readonly fields instead).
            Block.Blocks[id].Init();
        }
    }
}
