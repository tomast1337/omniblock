using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Items.Behaviors;
using BetaSharp.Network.Packets;
using BetaSharp.Registries;
using BetaSharp.Stats;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Items;

public class Item
{
    internal static JavaRandom itemRand = new();
    public static Item?[] ITEMS = new Item[32000];

    private static Block[]? s_spadeBlocksLazy;
    internal static Block[] s_spadeBlocks => s_spadeBlocksLazy ??=
        [Block.GrassBlock, Block.Dirt, Block.Sand, Block.Gravel, Block.Snow, Block.SnowBlock, Block.Clay, Block.Farmland];

    private static Block[]? s_pickaxeBlocksLazy;
    internal static Block[] s_pickaxeBlocks => s_pickaxeBlocksLazy ??= [ Block.Cobblestone, Block.DoubleSlab, Block.Slab, Block.Stone, Block.Sandstone, Block.MossyCobblestone, Block.IronOre, Block.IronBlock, Block.CoalOre, Block.GoldBlock, Block.GoldOre, Block.DiamondOre, Block.DiamondBlock, Block.Ice, Block.Netherrack, Block.LapisOre, Block.LapisBlock, Block.RedstoneOre, Block.CobblestoneStairs];

    private static Block[]? s_axeBlocksLazy;
    internal static Block[] s_axeBlocks => s_axeBlocksLazy ??= [Block.Planks, Block.Bookshelf, Block.Log, Block.Chest, Block.CraftingTable, Block.WoodenStairs, Block.Ladder, Block.Trapdoor, Block.Fence];

    /// <summary>
    /// Resolves an item by its registry path (e.g. <c>"apple"</c>, <c>"shovel_iron"</c> —
    /// see <c>BetaSharp/assets/item/betasharp/*.json</c> for the full list of names).
    /// Requires <see cref="Registries.DefaultRegistries.Initialize"/> to have run.
    ///
    /// TODO: This will become obsolete once Entities and Blocks are fully data-driven
    /// and resolve their drops/interactions via ResourceLocations directly from JSON data files.
    /// </summary>
    public static Item ByName(string name)
    {
        ItemDefinition? def = DefaultRegistries.Items.Get(new ResourceLocation(Namespace.BetaSharp, name))?.Value;
        if (def is null || ITEMS[def.ProtocolId] is not { } item)
        {
            throw new ArgumentException($"Unknown item: '{name}'", nameof(name));
        }

        return item;
    }

    private readonly ILogger<Item> _logger = Log.Instance.For<Item>();

    public readonly int Id;
    private IItemBehavior? _behavior;
    private Item _craftingReturnItem;
    public bool Handheld;
    public bool HasSubtypes;
    private int MaxCount = 64;
    private int _maxDamage;
    internal int _textureId;
    private string _translationKey;

    internal Item(int id)
    {
        this.Id = 256 + id;
        if (ITEMS[256 + id] != null)
        {
            _logger.LogInformation($"CONFLICT @ {id}");
        }

        ITEMS[256 + id] = this;
    }

    public virtual IReadOnlyList<string> GetItemAlias => _behavior?.GetItemAliases(this) ?? [];

    public Item SetBehavior(IItemBehavior behavior)
    {
        _behavior = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior => _behavior as TBehavior;

    public Item setTextureId(int textureId)
    {
        this._textureId = textureId;
        return this;
    }

    public Item setMaxCount(int maxCount)
    {
        this.MaxCount = maxCount;
        return this;
    }

    public Item setTexturePosition(int x, int y)
    {
        _textureId = x + y * 16;
        return this;
    }

    public virtual int getTextureId(int damage) => _behavior?.GetTextureId(this, damage) ?? _textureId;

    public int getTextureId(ItemStack stack) => getTextureId(stack.getDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta) => _behavior?.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta) ?? false;

    public virtual float getMiningSpeedMultiplier(ItemStack itemStack, Block block) => _behavior?.GetMiningSpeedMultiplier(this, itemStack, block) ?? 1.0F;

    public virtual ItemStack use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer) => _behavior?.Use(this, itemStack, world, entityPlayer) ?? itemStack;

    public int getMaxCount() => MaxCount;

    public virtual int getPlacementMetadata(int meta) => 0;

    public bool getHasSubtypes() => HasSubtypes;

    internal Item setHasSubtypes(bool has)
    {
        HasSubtypes = has;
        return this;
    }

    public int getMaxDamage() => _maxDamage;

    internal Item setMaxDamage(int dmg)
    {
        _maxDamage = dmg;
        return this;
    }

    public bool isDamagable() => _maxDamage > 0 && !HasSubtypes;

    public virtual bool postHit(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer) => _behavior?.PostHit(this, itemStack, entityLiving, entityPlayer) ?? false;

    public virtual bool postMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving) => _behavior?.PostMine(this, itemStack, blockId, x, y, z, entityLiving) ?? false;

    public virtual int getAttackDamage(Entity entity) => _behavior?.GetAttackDamage(this, entity) ?? 1;

    public virtual bool isSuitableFor(Block block) => _behavior?.IsSuitableFor(this, block) ?? false;

    public virtual void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer) => _behavior?.UseOnEntity(this, itemStack, entityLiving, entityPlayer);

    public Item setHandheld()
    {
        Handheld = true;
        return this;
    }

    public virtual bool isHandheld() => _behavior?.IsHandheld(this) ?? Handheld;

    public virtual bool isHandheldRod() => _behavior?.IsHandheldRod(this) ?? false;

    public Item setItemName(string name)
    {
        _translationKey = "item." + name;
        return this;
    }

    public virtual string getItemName() => _translationKey;

    public virtual string getItemNameIS(ItemStack itemStack) => _behavior?.GetItemNameIS(this, itemStack) ?? _translationKey;

    public Item setCraftingReturnItem(Item item)
    {
        if (MaxCount > 1)
        {
            throw new ArgumentException("Max stack size must be 1 for items with crafting results");
        }

        _craftingReturnItem = item;
        return this;
    }

    public Item getContainerItem() => _craftingReturnItem;

    public bool hasContainerItem() => _craftingReturnItem != null;

    public string getStatName()
        => StatCollector.TranslateToLocal(getItemName() + ".name");

    public virtual int getColorMultiplier(int color) => 0xFFFFFF;

    public virtual void inventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate) => _behavior?.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);

    public virtual void onCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer) => _behavior?.OnCraft(this, itemStack, world, entityPlayer);

    public virtual bool isNetworkSynced() => _behavior?.IsNetworkSynced(this) ?? false;

    public virtual Packet? getUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player) => _behavior?.GetUpdatePacket(this, stack, world, player);

    internal static Func<Block, bool> PickaxeSuitableFor(ToolMaterial material) => block =>
    {
        if (block == Block.Obsidian)
        {
            return material.HarvestLevel == 3;
        }

        if (block == Block.DiamondBlock || block == Block.DiamondOre)
        {
            return material.HarvestLevel >= 2;
        }

        if (block == Block.GoldBlock || block == Block.GoldOre)
        {
            return material.HarvestLevel >= 2;
        }

        if (block == Block.IronBlock || block == Block.IronOre)
        {
            return material.HarvestLevel >= 1;
        }

        if (block == Block.LapisBlock || block == Block.LapisOre)
        {
            return material.HarvestLevel >= 1;
        }

        if (block == Block.RedstoneOre || block == Block.LitRedstoneOre)
        {
            return material.HarvestLevel >= 2;
        }

        return block.material == Material.Stone || block.material == Material.Metal;
    };
}
