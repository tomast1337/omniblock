using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Blocks;

public static class BlockRegistry
{
    public const int ProtocolIdCapacity = 256;

    private static readonly CanonicalRegistry<BlockDefinition> s_registry = new("block");

    private static readonly Dictionary<int, string> s_idToName = [];
    private static ContentRuntimeBuilder? s_bootstrapBuilder;

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
        return GetBootstrapBuilder().GetBlock(key);
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
        return GetBootstrapBuilder().GetBlockByProtocolId(protocolId);
    }

    public static bool TryGetByProtocolId(int protocolId, out Block? block)
    {
        if (ContentRuntime.TryGetCurrent(out ContentRuntime? runtime))
        {
            return runtime.Blocks.TryGetByProtocolId(protocolId, out block);
        }

        ContentRuntimeBuilder? builder = Volatile.Read(ref s_bootstrapBuilder);
        if (builder is not null) return builder.TryGetBlockByProtocolId(protocolId, out block);

        block = null;
        return false;
    }

    public static bool IsOpaque(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) && block.IsOpaque;

    public static int GetOpacity(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) ? block.Opacity : 0;

    public static int GetLightEmission(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) ? block.LightEmission : 0;

    public static bool AllowsVision(int protocolId) =>
        !TryGetByProtocolId(protocolId, out Block? block) || block.AllowsVision;

    public static bool HasBlockEntity(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) && block.HasBlockEntity;

    public static bool TicksRandomly(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) && block.TickRandomly;

    public static bool IgnoresMetaUpdates(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block) && block.IgnoreMetaUpdates;

    /// <summary>Reverse lookup: the registry name a block was defined under, given its protocol id.</summary>
    public static string? TryGetName(int protocolId) => s_idToName.GetValueOrDefault(protocolId);

    internal static void Initialize(ContentRuntimeBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (s_registry.IsInitialized) return;
        if (Interlocked.CompareExchange(ref s_bootstrapBuilder, content, null) is not null)
            throw new InvalidOperationException("Block content bootstrap is already in progress.");

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
    }

    internal static void CompleteBootstrap(ContentRuntimeBuilder content)
    {
        ContentRuntimeBuilder? active = Interlocked.CompareExchange(ref s_bootstrapBuilder, null, content);
        if (active is not null && !ReferenceEquals(active, content))
            throw new InvalidOperationException("The active block content builder changed during bootstrap.");
    }

    private static ContentRuntimeBuilder GetBootstrapBuilder() =>
        Volatile.Read(ref s_bootstrapBuilder)
        ?? throw new InvalidOperationException("Block content runtime is neither being built nor published.");

    private static void LoadAndBuild(IEnumerable<BlockDefinition> definitions, ContentRuntimeBuilder content)
    {
        List<BlockDefinition> defs = definitions.ToList();

        foreach (BlockDefinition def in defs)
        {
            content.AddBlock(def, BlockFactory.Create(def, content.BlockBuildContext));
        }

        ItemLookup.Initialize();

        foreach (BlockDefinition def in defs)
        {
            Block block = content.GetBlockByProtocolId(def.ProtocolId);
            BlockFactory.AttachBehaviors(
                block,
                def,
                content.BlockBehaviorProviders,
                content.BlockBuildContext);
        }

        content.BuildBlockItems(defs);
    }
}
