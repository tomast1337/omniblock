using OmniBlock.Blocks.Materials;
using OmniBlock.Items;
using OmniBlock.Textures;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
/// Dependencies available while a block behavior definition is being compiled into its runtime
/// behavior. Keeping resolution here lets providers remain independent of process-global content
/// storage and gives a future content runtime one explicit adapter to replace.
/// </summary>
public readonly struct BehaviorBuildContext
{
    private readonly Func<ResourceLocation, Block> _resolveBlock;
    private readonly Func<ResourceLocation, Item> _resolveItem;
    private readonly Func<ResourceLocation, Material> _resolveMaterial;
    private readonly Func<string, int> _resolveTerrainTexture;

    public BehaviorBuildContext(
        Func<ResourceLocation, Block> resolveBlock,
        Func<ResourceLocation, Item> resolveItem,
        Func<ResourceLocation, Material> resolveMaterial,
        Func<string, int> resolveTerrainTexture,
        IBlockRuntimeView? blocks = null)
    {
        ArgumentNullException.ThrowIfNull(resolveBlock);
        ArgumentNullException.ThrowIfNull(resolveItem);
        ArgumentNullException.ThrowIfNull(resolveMaterial);
        ArgumentNullException.ThrowIfNull(resolveTerrainTexture);

        _resolveBlock = resolveBlock;
        _resolveItem = resolveItem;
        _resolveMaterial = resolveMaterial;
        _resolveTerrainTexture = resolveTerrainTexture;
        Blocks = blocks ?? new DelegateBlockRuntimeView(resolveBlock);
    }

    public Block ResolveBlock(ResourceLocation key)
    {
        Func<ResourceLocation, Block> resolver = _resolveBlock ?? throw Uninitialized();
        return Resolve(() => resolver(key), "block", key.ToString());
    }

    public Item ResolveItem(ResourceLocation key)
    {
        Func<ResourceLocation, Item> resolver = _resolveItem ?? throw Uninitialized();
        return Resolve(() => resolver(key), "item", key.ToString());
    }

    public Material ResolveMaterial(ResourceLocation key)
    {
        Func<ResourceLocation, Material> resolver = _resolveMaterial ?? throw Uninitialized();
        return Resolve(() => resolver(key), "material", key.ToString());
    }

    public int ResolveTerrainTexture(string key)
    {
        Func<string, int> resolver = _resolveTerrainTexture ?? throw Uninitialized();
        int id = Resolve(() => resolver(key), "terrain texture", key);
        return id >= 0 ? id : throw new KeyNotFoundException($"Unknown terrain texture '{key}'.");
    }

    private static T Resolve<T>(Func<T> resolver, string kind, string key)
    {
        try
        {
            return resolver() ?? throw new KeyNotFoundException();
        }
        catch (Exception error) when (error is KeyNotFoundException or ArgumentException)
        {
            if (error is ArgumentException)
                throw new ArgumentException($"Unknown {kind} '{key}'.", error);
            throw new KeyNotFoundException($"Unknown {kind} '{key}'.", error);
        }
    }

    public IBlockRuntimeView Blocks { get; }

    internal BehaviorBuildContext WithBlocks(IBlockRuntimeView blocks) =>
        new(_resolveBlock, _resolveItem, _resolveMaterial, _resolveTerrainTexture, blocks);

    internal static BehaviorBuildContext BuiltIns { get; } = new(
        static key => BlockRegistry.Get(key.Path),
        static key => Item.ByName(key.Path),
        static key => MaterialRegistry.Get(key.Path),
        static key => Atlases.Terrain.IndexOf(key));

    private static InvalidOperationException Uninitialized() =>
        new($"{nameof(BehaviorBuildContext)} must be initialized before resolving dependencies.");

    private sealed class DelegateBlockRuntimeView(Func<ResourceLocation, Block> resolveBlock) : IBlockRuntimeView
    {
        public Block Get(ResourceLocation key) => resolveBlock(key);
        public Block GetByProtocolId(int protocolId) => BlockRegistry.GetByProtocolId(protocolId);
        public bool TryGetByProtocolId(int protocolId, out Block? block) =>
            BlockRegistry.TryGetByProtocolId(protocolId, out block);
    }
}
