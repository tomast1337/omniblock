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
        Func<string, int> resolveTerrainTexture)
    {
        ArgumentNullException.ThrowIfNull(resolveBlock);
        ArgumentNullException.ThrowIfNull(resolveItem);
        ArgumentNullException.ThrowIfNull(resolveMaterial);
        ArgumentNullException.ThrowIfNull(resolveTerrainTexture);

        _resolveBlock = resolveBlock;
        _resolveItem = resolveItem;
        _resolveMaterial = resolveMaterial;
        _resolveTerrainTexture = resolveTerrainTexture;
    }

    public Block ResolveBlock(ResourceLocation key) =>
        (_resolveBlock ?? throw Uninitialized()).Invoke(key);

    public Item ResolveItem(ResourceLocation key) =>
        (_resolveItem ?? throw Uninitialized()).Invoke(key);

    public Material ResolveMaterial(ResourceLocation key) =>
        (_resolveMaterial ?? throw Uninitialized()).Invoke(key);

    public int ResolveTerrainTexture(string key) =>
        (_resolveTerrainTexture ?? throw Uninitialized()).Invoke(key);

    internal static BehaviorBuildContext BuiltIns { get; } = new(
        static key => BlockRegistry.Get(key.Path),
        static key => Item.ByName(key.Path),
        static key => MaterialRegistry.Get(key.Path),
        static key => Atlases.Terrain.IndexOf(key));

    private static InvalidOperationException Uninitialized() =>
        new($"{nameof(BehaviorBuildContext)} must be initialized before resolving dependencies.");
}
