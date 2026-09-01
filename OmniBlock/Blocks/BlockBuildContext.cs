using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Entities;
using OmniBlock.Items;

namespace OmniBlock.Blocks;

/// <summary>All external dependencies used to compile a block definition.</summary>
public readonly struct BlockBuildContext
{
    private readonly Func<ResourceLocation, BlockSoundGroup> _resolveSoundGroup;
    private readonly Func<ResourceLocation, int> _resolveLootItemOrBlockId;
    private readonly Func<ResourceLocation, Func<BlockEntity>> _resolveBlockEntityFactory;

    public BlockBuildContext(
        BehaviorBuildContext behaviors,
        Func<ResourceLocation, BlockSoundGroup> resolveSoundGroup,
        Func<ResourceLocation, int> resolveLootItemOrBlockId,
        Func<ResourceLocation, Func<BlockEntity>> resolveBlockEntityFactory)
    {
        ArgumentNullException.ThrowIfNull(resolveSoundGroup);
        ArgumentNullException.ThrowIfNull(resolveLootItemOrBlockId);
        ArgumentNullException.ThrowIfNull(resolveBlockEntityFactory);
        Behaviors = behaviors;
        _resolveSoundGroup = resolveSoundGroup;
        _resolveLootItemOrBlockId = resolveLootItemOrBlockId;
        _resolveBlockEntityFactory = resolveBlockEntityFactory;
    }

    public BehaviorBuildContext Behaviors { get; }

    public BlockSoundGroup ResolveSoundGroup(ResourceLocation key)
    {
        Func<ResourceLocation, BlockSoundGroup> resolver = _resolveSoundGroup ?? throw Uninitialized();
        return Resolve(() => resolver(key), "sound group", key);
    }

    public int ResolveLootItemOrBlockId(ResourceLocation key)
    {
        Func<ResourceLocation, int> resolver = _resolveLootItemOrBlockId ?? throw Uninitialized();
        return Resolve(() => resolver(key), "loot item or block", key);
    }

    public Func<BlockEntity> ResolveBlockEntityFactory(ResourceLocation key)
    {
        Func<ResourceLocation, Func<BlockEntity>> resolver = _resolveBlockEntityFactory ?? throw Uninitialized();
        return Resolve(() => resolver(key), "block entity", key);
    }

    private static T Resolve<T>(Func<T> resolver, string kind, ResourceLocation key)
    {
        try
        {
            return resolver() ?? throw new KeyNotFoundException();
        }
        catch (Exception error) when (error is KeyNotFoundException or ArgumentException)
        {
            throw new KeyNotFoundException($"Unknown {kind} '{key}'.", error);
        }
    }

    internal static BlockBuildContext BuiltIns(BehaviorBuildContext behaviors) => new(
        behaviors,
        static key => SoundGroupRegistry.Get(key.Path),
        static key => ItemLookup.TryGetItemId(key.Path, out int id)
            ? id
            : throw new ArgumentException($"Unknown item or block: '{key}'"),
        static key => BlockEntityFactoryRegistry.Get(key.Path));

    private static InvalidOperationException Uninitialized() =>
        new($"{nameof(BlockBuildContext)} must be initialized before resolving dependencies.");
}
