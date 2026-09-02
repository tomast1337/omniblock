using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Registries;
using OmniBlock.Recipes;

namespace OmniBlock.Items;

/// <summary>All external dependencies available while compiling an item definition.</summary>
public readonly struct ItemBuildContext
{
    private readonly Func<ResourceLocation, Block> _resolveBlock;
    private readonly Func<ResourceLocation, Item> _resolveBlockItem;
    private readonly Func<ResourceLocation, Item> _resolveItem;
    private readonly Func<ResourceLocation, ToolMaterial> _resolveToolMaterial;
    private readonly Func<ResourceLocation, ArmorMaterial> _resolveArmorMaterial;
    private readonly Func<ResourceLocation, Material> _resolveBlockMaterial;
    private readonly Func<string, int> _resolveItemTexture;
    private readonly Func<ResourceLocation, EntityType> _resolveEntityType;
    private readonly Func<ResourceLocation, BlockEntityType> _resolveBlockEntityType;
    private readonly Func<ResourceLocation, RecipeDefinition> _resolveRecipeDependency;
    private readonly Func<ResourceLocation, object> _resolveInteractionDependency;
    private readonly Action<ResourceLocation>? _validateEntityType;

    public ItemBuildContext(
        Func<ResourceLocation, Block> resolveBlock,
        Func<ResourceLocation, Item> resolveBlockItem,
        Func<ResourceLocation, Item> resolveItem,
        Func<ResourceLocation, ToolMaterial> resolveToolMaterial,
        Func<ResourceLocation, ArmorMaterial> resolveArmorMaterial,
        Func<ResourceLocation, Material> resolveBlockMaterial,
        Func<string, int> resolveItemTexture,
        Func<ResourceLocation, EntityType> resolveEntityType,
        Func<ResourceLocation, BlockEntityType> resolveBlockEntityType,
        Func<ResourceLocation, RecipeDefinition> resolveRecipeDependency,
        Func<ResourceLocation, object> resolveInteractionDependency,
        Action<ResourceLocation>? validateEntityType = null)
    {
        ArgumentNullException.ThrowIfNull(resolveBlock);
        ArgumentNullException.ThrowIfNull(resolveBlockItem);
        ArgumentNullException.ThrowIfNull(resolveItem);
        ArgumentNullException.ThrowIfNull(resolveToolMaterial);
        ArgumentNullException.ThrowIfNull(resolveArmorMaterial);
        ArgumentNullException.ThrowIfNull(resolveBlockMaterial);
        ArgumentNullException.ThrowIfNull(resolveItemTexture);
        ArgumentNullException.ThrowIfNull(resolveEntityType);
        ArgumentNullException.ThrowIfNull(resolveBlockEntityType);
        ArgumentNullException.ThrowIfNull(resolveRecipeDependency);
        ArgumentNullException.ThrowIfNull(resolveInteractionDependency);

        _resolveBlock = resolveBlock;
        _resolveBlockItem = resolveBlockItem;
        _resolveItem = resolveItem;
        _resolveToolMaterial = resolveToolMaterial;
        _resolveArmorMaterial = resolveArmorMaterial;
        _resolveBlockMaterial = resolveBlockMaterial;
        _resolveItemTexture = resolveItemTexture;
        _resolveEntityType = resolveEntityType;
        _resolveBlockEntityType = resolveBlockEntityType;
        _resolveRecipeDependency = resolveRecipeDependency;
        _resolveInteractionDependency = resolveInteractionDependency;
        _validateEntityType = validateEntityType;
    }

    public Block ResolveBlock(ResourceLocation key) => Resolve(_resolveBlock, key, "block");
    public Item ResolveBlockItem(ResourceLocation key) => Resolve(_resolveBlockItem, key, "block item");
    public Item ResolveItem(ResourceLocation key) => Resolve(_resolveItem, key, "item");
    public ToolMaterial ResolveToolMaterial(ResourceLocation key) => Resolve(_resolveToolMaterial, key, "tool material");
    public ArmorMaterial ResolveArmorMaterial(ResourceLocation key) => Resolve(_resolveArmorMaterial, key, "armor material");
    public Material ResolveBlockMaterial(ResourceLocation key) => Resolve(_resolveBlockMaterial, key, "block material");
    public EntityType ResolveEntityType(ResourceLocation key) => Resolve(_resolveEntityType, key, "entity type");
    public void ValidateEntityType(ResourceLocation key)
    {
        if (_validateEntityType is null)
        {
            _ = ResolveEntityType(key);
            return;
        }
        Action<ResourceLocation> validator = _validateEntityType;
        ResolveCore(() => { validator(key); return true; }, "entity type", key.ToString());
    }
    public BlockEntityType ResolveBlockEntityType(ResourceLocation key) => Resolve(_resolveBlockEntityType, key, "block-entity type");
    public RecipeDefinition ResolveRecipeDependency(ResourceLocation key) => Resolve(_resolveRecipeDependency, key, "recipe dependency");
    public object ResolveInteractionDependency(ResourceLocation key) => Resolve(_resolveInteractionDependency, key, "interaction dependency");

    public int ResolveItemTexture(string key)
    {
        Func<string, int> resolver = _resolveItemTexture ?? throw Uninitialized();
        int id = ResolveCore(() => resolver(key), "item texture", key);
        return id >= 0 ? id : throw new KeyNotFoundException($"Unknown item texture '{key}'.");
    }

    private static T Resolve<T>(Func<ResourceLocation, T>? resolver, ResourceLocation key, string kind) where T : class =>
        ResolveCore(() => (resolver ?? throw Uninitialized())(key), kind, key.ToString());

    private static T ResolveCore<T>(Func<T> resolver, string kind, string key)
    {
        try
        {
            return resolver() ?? throw new KeyNotFoundException();
        }
        catch (Exception error) when (error is KeyNotFoundException or ArgumentException or IndexOutOfRangeException)
        {
            throw new KeyNotFoundException($"Unknown {kind} '{key}'.", error);
        }
    }

    internal static ItemBuildContext BuiltIns { get; } = new(
        static key => BlockRegistry.Get(key.Path),
        static key => ContentRuntime.Current.Items.GetByProtocolId(ContentRuntime.Current.Blocks.Get(key).Id),
        static key => Item.ByName(key.Path),
        static key => ToolMaterialRegistry.Get(key.Path),
        static key => ArmorMaterialRegistry.Get(key.Path),
        static key => MaterialRegistry.Get(key.Path),
        static key => Textures.Atlases.Items.IndexOf(key),
        static key => EntityRegistry.ByName(key.Path),
        static key => DefaultRegistries.BlockEntityTypes.Get(key)?.Value
                      ?? throw new KeyNotFoundException($"Unknown block-entity type '{key}'."),
        static key => throw new KeyNotFoundException($"Unknown recipe dependency '{key}'."),
        static key => throw new KeyNotFoundException($"Unknown interaction dependency '{key}'."),
        static key => _ = EntityDefinitionRegistry.Get(key.Path));

    private static InvalidOperationException Uninitialized() =>
        new($"{nameof(ItemBuildContext)} must be initialized before resolving dependencies.");
}
