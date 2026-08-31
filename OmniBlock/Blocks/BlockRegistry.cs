using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Blocks;

public static class BlockRegistry
{
    public const int ProtocolIdCapacity = 256;

    private static readonly CanonicalRegistry<BlockDefinition> s_registry = new("block");

    private static readonly Dictionary<int, string> s_idToName = [];

    /// <summary>Returns the live, constructed <see cref="Block" /> registered under <paramref name="name" />.</summary>
    public static Block Get(string name)
    {
        ResourceLocation key = ResourceLocation.Parse(name);
        if (ContentRuntime.TryGetCurrent(out ContentRuntime? runtime))
        {
            return runtime.Blocks.Get(key);
        }

        // Bootstrap still initializes stats, achievements, and entity definitions after blocks but
        // before ContentRuntime is published. Keep that construction-only path isolated here; once
        // publication succeeds every lookup above is served by the immutable runtime snapshot.
        BlockDefinition definition = s_registry.Get(key.Path);
        return Block.GetDuringBootstrap(definition.ProtocolId)
               ?? throw new InvalidOperationException(
                   $"Block '{key}' is registered but its content runtime has not been built yet.");
    }

    /// <summary>Returns the live block registered under the protocol-level numeric id.</summary>
    public static Block GetByProtocolId(int protocolId)
    {
        if (ContentRuntime.TryGetCurrent(out ContentRuntime? runtime))
        {
            return runtime.Blocks.GetByProtocolId(protocolId);
        }

        // Block-derived items are constructed while the runtime snapshot is still being built.
        // Keep that bootstrap exception behind this boundary so consumers never own catalog state.
        return Block.GetDuringBootstrap(protocolId) is { } block
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");
    }

    public static bool TryGetByProtocolId(int protocolId, out Block? block)
    {
        if (ContentRuntime.TryGetCurrent(out ContentRuntime? runtime))
        {
            return runtime.Blocks.TryGetByProtocolId(protocolId, out block);
        }

        block = Block.GetDuringBootstrap(protocolId);
        return block is not null;
    }

    /// <summary>Reverse lookup: the registry name a block was defined under, given its protocol id.</summary>
    public static string? TryGetName(int protocolId) => s_idToName.GetValueOrDefault(protocolId);

    internal static void Initialize(ContentRuntimeBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
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

        LoadAndBuild(definitions, content);
        BridgeToItems(definitions);
        Block.BlocksAllowVision[0] = true;
    }

    private static void LoadAndBuild(IEnumerable<BlockDefinition> definitions, ContentRuntimeBuilder content)
    {
        List<BlockDefinition> defs = definitions.ToList();

        foreach (BlockDefinition def in defs)
        {
            BlockFactory.Create(def);
        }

        ItemLookup.Initialize();

        foreach (BlockDefinition def in defs)
        {
            Block block = Block.GetDuringBootstrap(def.ProtocolId)
                          ?? throw new InvalidOperationException($"Block '{def.Name}' was not constructed.");
            BlockFactory.AttachBehaviors(
                block,
                def,
                content.BlockBehaviorProviders,
                content.BehaviorBuildContext);
            content.AddBlock(def, block);
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
            GetByProtocolId(id).Init();
        }
    }
}
