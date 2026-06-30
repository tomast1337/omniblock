using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Items.Behaviors;
using BetaSharp.Network.Packets;
using BetaSharp.Stats;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Items;

public class Item
{
    internal static JavaRandom itemRand = new();
    public static Item?[] ITEMS = new Item[32000];

    private static readonly Block[] s_spadeBlocks = [Block.GrassBlock, Block.Dirt, Block.Sand, Block.Gravel, Block.Snow, Block.SnowBlock, Block.Clay, Block.Farmland];

    private static readonly Block[] s_pickaxeBlocks =
    [
        Block.Cobblestone, Block.DoubleSlab, Block.Slab, Block.Stone, Block.Sandstone, Block.MossyCobblestone, Block.IronOre, Block.IronBlock, Block.CoalOre, Block.GoldBlock, Block.GoldOre, Block.DiamondOre, Block.DiamondBlock, Block.Ice,
        Block.Netherrack, Block.LapisOre, Block.LapisBlock, Block.RedstoneOre, Block.CobblestoneStairs
    ];

    private static readonly Block[] s_axeBlocks = [Block.Planks, Block.Bookshelf, Block.Log, Block.Chest, Block.CraftingTable, Block.WoodenStairs, Block.Ladder, Block.Trapdoor, Block.Fence];

    public static Item IronShovel = new Item(0).SetBehavior(new ToolBehavior(ToolMaterial.IRON, 1, s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock)).setTexturePosition(2, 5).setItemName("shovelIron");
    public static Item IronPickaxe = new Item(1).SetBehavior(new ToolBehavior(ToolMaterial.IRON, 2, s_pickaxeBlocks, PickaxeSuitableFor(ToolMaterial.IRON))).setTexturePosition(2, 6).setItemName("pickaxeIron");
    public static Item IronAxe = new Item(2).SetBehavior(new ToolBehavior(ToolMaterial.IRON, 3, s_axeBlocks)).setTexturePosition(2, 7).setItemName("hatchetIron");
    public static Item FlintAndSteel = new Item(3).SetBehavior(new FlintAndSteelBehavior()).setTexturePosition(5, 0).setItemName("flintAndSteel");
    public static Item Apple = new Item(4).SetBehavior(new FoodBehavior(4, false)).setTexturePosition(10, 0).setItemName("apple");
    public static Item BOW = new Item(5).SetBehavior(new BowBehavior()).setTexturePosition(5, 1).setItemName("bow");
    public static Item ARROW = new Item(6).setTexturePosition(5, 2).setItemName("arrow");
    public static Item Coal = new Item(7).SetBehavior(new CoalBehavior()).setTexturePosition(7, 0).setItemName("coal");
    public static Item Diamond = new Item(8).setTexturePosition(7, 3).setItemName("emerald");
    public static Item IronIngot = new Item(9).setTexturePosition(7, 1).setItemName("ingotIron");
    public static Item GoldIngot = new Item(10).setTexturePosition(7, 2).setItemName("ingotGold");
    public static Item IronSword = new Item(11).SetBehavior(new SwordBehavior(ToolMaterial.IRON)).setTexturePosition(2, 4).setItemName("swordIron");
    public static Item WoodenSword = new Item(12).SetBehavior(new SwordBehavior(ToolMaterial.WOOD)).setTexturePosition(0, 4).setItemName("swordWood");
    public static Item WoodenShovel = new Item(13).SetBehavior(new ToolBehavior(ToolMaterial.WOOD, 1, s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock)).setTexturePosition(0, 5).setItemName("shovelWood");
    public static Item WoodenPickaxe = new Item(14).SetBehavior(new ToolBehavior(ToolMaterial.WOOD, 2, s_pickaxeBlocks, PickaxeSuitableFor(ToolMaterial.WOOD))).setTexturePosition(0, 6).setItemName("pickaxeWood");
    public static Item WoodenAxe = new Item(15).SetBehavior(new ToolBehavior(ToolMaterial.WOOD, 3, s_axeBlocks)).setTexturePosition(0, 7).setItemName("hatchetWood");
    public static Item StoneSword = new Item(16).SetBehavior(new SwordBehavior(ToolMaterial.STONE)).setTexturePosition(1, 4).setItemName("swordStone");
    public static Item StoneShovel = new Item(17).SetBehavior(new ToolBehavior(ToolMaterial.STONE, 1, s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock)).setTexturePosition(1, 5).setItemName("shovelStone");
    public static Item StonePickaxe = new Item(18).SetBehavior(new ToolBehavior(ToolMaterial.STONE, 2, s_pickaxeBlocks, PickaxeSuitableFor(ToolMaterial.STONE))).setTexturePosition(1, 6).setItemName("pickaxeStone");
    public static Item StoneAxe = new Item(19).SetBehavior(new ToolBehavior(ToolMaterial.STONE, 3, s_axeBlocks)).setTexturePosition(1, 7).setItemName("hatchetStone");
    public static Item DiamondSword = new Item(20).SetBehavior(new SwordBehavior(ToolMaterial.EMERALD)).setTexturePosition(3, 4).setItemName("swordDiamond");
    public static Item DiamondShovel = new Item(21).SetBehavior(new ToolBehavior(ToolMaterial.EMERALD, 1, s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock)).setTexturePosition(3, 5).setItemName("shovelDiamond");
    public static Item DiamondPickaxe = new Item(22).SetBehavior(new ToolBehavior(ToolMaterial.EMERALD, 2, s_pickaxeBlocks, PickaxeSuitableFor(ToolMaterial.EMERALD))).setTexturePosition(3, 6).setItemName("pickaxeDiamond");
    public static Item DiamondAxe = new Item(23).SetBehavior(new ToolBehavior(ToolMaterial.EMERALD, 3, s_axeBlocks)).setTexturePosition(3, 7).setItemName("hatchetDiamond");
    public static Item Stick = new Item(24).setTexturePosition(5, 3).setHandheld().setItemName("stick");
    public static Item Bowl = new Item(25).setTexturePosition(7, 4).setItemName("bowl");
    public static Item MushroomStew = new Item(26).SetBehavior(new FoodBehavior(10, false, returnItem: Bowl)).setTexturePosition(8, 4).setItemName("mushroomStew");
    public static Item GoldenSword = new Item(27).SetBehavior(new SwordBehavior(ToolMaterial.GOLD)).setTexturePosition(4, 4).setItemName("swordGold");
    public static Item GoldenShovel = new Item(28).SetBehavior(new ToolBehavior(ToolMaterial.GOLD, 1, s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock)).setTexturePosition(4, 5).setItemName("shovelGold");
    public static Item GoldenPickaxe = new Item(29).SetBehavior(new ToolBehavior(ToolMaterial.GOLD, 2, s_pickaxeBlocks, PickaxeSuitableFor(ToolMaterial.GOLD))).setTexturePosition(4, 6).setItemName("pickaxeGold");
    public static Item GoldenAxe = new Item(30).SetBehavior(new ToolBehavior(ToolMaterial.GOLD, 3, s_axeBlocks)).setTexturePosition(4, 7).setItemName("hatchetGold");
    public static Item String = new Item(31).setTexturePosition(8, 0).setItemName("string");
    public static Item Feather = new Item(32).setTexturePosition(8, 1).setItemName("feather");
    public static Item Gunpowder = new Item(33).setTexturePosition(8, 2).setItemName("sulphur");
    public static Item WoodenHoe = new Item(34).SetBehavior(new HoeBehavior(ToolMaterial.WOOD)).setTexturePosition(0, 8).setItemName("hoeWood");
    public static Item StoneHoe = new Item(35).SetBehavior(new HoeBehavior(ToolMaterial.STONE)).setTexturePosition(1, 8).setItemName("hoeStone");
    public static Item IronHoe = new Item(36).SetBehavior(new HoeBehavior(ToolMaterial.IRON)).setTexturePosition(2, 8).setItemName("hoeIron");
    public static Item DiamondHoe = new Item(37).SetBehavior(new HoeBehavior(ToolMaterial.EMERALD)).setTexturePosition(3, 8).setItemName("hoeDiamond");
    public static Item GoldenHoe = new Item(38).SetBehavior(new HoeBehavior(ToolMaterial.GOLD)).setTexturePosition(4, 8).setItemName("hoeGold");
    public static Item Seeds = new Item(39).SetBehavior(new SeedsBehavior(Block.Wheat.id)).setTexturePosition(9, 0).setItemName("seeds");
    public static Item Wheat = new Item(40).setTexturePosition(9, 1).setItemName("wheat");
    public static Item Bread = new Item(41).SetBehavior(new FoodBehavior(5, false)).setTexturePosition(9, 2).setItemName("bread");
    public static Item LeatherHelmet = new Item(42).SetBehavior(new ArmorBehavior(0, 0, 0)).setTexturePosition(0, 0).setItemName("helmetCloth");
    public static Item LeatherChestplate = new Item(43).SetBehavior(new ArmorBehavior(0, 0, 1)).setTexturePosition(0, 1).setItemName("chestplateCloth");
    public static Item LeatherLeggings = new Item(44).SetBehavior(new ArmorBehavior(0, 0, 2)).setTexturePosition(0, 2).setItemName("leggingsCloth");
    public static Item LeatherBoots = new Item(45).SetBehavior(new ArmorBehavior(0, 0, 3)).setTexturePosition(0, 3).setItemName("bootsCloth");
    public static Item ChainHelmet = new Item(46).SetBehavior(new ArmorBehavior(1, 1, 0)).setTexturePosition(1, 0).setItemName("helmetChain");
    public static Item ChainChestplate = new Item(47).SetBehavior(new ArmorBehavior(1, 1, 1)).setTexturePosition(1, 1).setItemName("chestplateChain");
    public static Item ChainLeggings = new Item(48).SetBehavior(new ArmorBehavior(1, 1, 2)).setTexturePosition(1, 2).setItemName("leggingsChain");
    public static Item ChainBoots = new Item(49).SetBehavior(new ArmorBehavior(1, 1, 3)).setTexturePosition(1, 3).setItemName("bootsChain");
    public static Item IronHelmet = new Item(50).SetBehavior(new ArmorBehavior(2, 2, 0)).setTexturePosition(2, 0).setItemName("helmetIron");
    public static Item IronChestplate = new Item(51).SetBehavior(new ArmorBehavior(2, 2, 1)).setTexturePosition(2, 1).setItemName("chestplateIron");
    public static Item IronLeggings = new Item(52).SetBehavior(new ArmorBehavior(2, 2, 2)).setTexturePosition(2, 2).setItemName("leggingsIron");
    public static Item IronBoots = new Item(53).SetBehavior(new ArmorBehavior(2, 2, 3)).setTexturePosition(2, 3).setItemName("bootsIron");
    public static Item DiamondHelmet = new Item(54).SetBehavior(new ArmorBehavior(3, 3, 0)).setTexturePosition(3, 0).setItemName("helmetDiamond");
    public static Item DiamondChestplate = new Item(55).SetBehavior(new ArmorBehavior(3, 3, 1)).setTexturePosition(3, 1).setItemName("chestplateDiamond");
    public static Item DiamondLeggings = new Item(56).SetBehavior(new ArmorBehavior(3, 3, 2)).setTexturePosition(3, 2).setItemName("leggingsDiamond");
    public static Item DiamondBoots = new Item(57).SetBehavior(new ArmorBehavior(3, 3, 3)).setTexturePosition(3, 3).setItemName("bootsDiamond");
    public static Item GoldenHelmet = new Item(58).SetBehavior(new ArmorBehavior(1, 4, 0)).setTexturePosition(4, 0).setItemName("helmetGold");
    public static Item GoldenChestplate = new Item(59).SetBehavior(new ArmorBehavior(1, 4, 1)).setTexturePosition(4, 1).setItemName("chestplateGold");
    public static Item GoldenLeggings = new Item(60).SetBehavior(new ArmorBehavior(1, 4, 2)).setTexturePosition(4, 2).setItemName("leggingsGold");
    public static Item GoldenBoots = new Item(61).SetBehavior(new ArmorBehavior(1, 4, 3)).setTexturePosition(4, 3).setItemName("bootsGold");
    public static Item Flint = new Item(62).setTexturePosition(6, 0).setItemName("flint");
    public static Item RawPorkchop = new Item(63).SetBehavior(new FoodBehavior(3, true)).setTexturePosition(7, 5).setItemName("porkchopRaw");
    public static Item CookedPorkchop = new Item(64).SetBehavior(new FoodBehavior(8, true)).setTexturePosition(8, 5).setItemName("porkchopCooked");
    public static Item Painting = new Item(65).SetBehavior(new PaintingBehavior()).setTexturePosition(10, 1).setItemName("painting");
    public static Item GoldenApple = new Item(66).SetBehavior(new FoodBehavior(42, false)).setTexturePosition(11, 0).setItemName("appleGold");
    public static Item Sign = new Item(67).SetBehavior(new SignBehavior()).setTexturePosition(10, 2).setItemName("sign");
    public static Item WoodenDoor = new Item(68).SetBehavior(new DoorBehavior(Material.Wood)).setTexturePosition(11, 2).setItemName("doorWood");
    public static Item Bucket = new Item(69).SetBehavior(new BucketBehavior(0)).setTexturePosition(10, 4).setItemName("bucket");
    public static Item WaterBucket = new Item(70).SetBehavior(new BucketBehavior(Block.FlowingWater.id)).setTexturePosition(11, 4).setItemName("bucketWater").setCraftingReturnItem(Bucket);
    public static Item LavaBucket = new Item(71).SetBehavior(new BucketBehavior(Block.FlowingLava.id)).setTexturePosition(12, 4).setItemName("bucketLava").setCraftingReturnItem(Bucket);
    public static Item Minecart = new Item(72).SetBehavior(new MinecartBehavior(0)).setTexturePosition(7, 8).setItemName("minecart");
    public static Item Saddle = new Item(73).SetBehavior(new SaddleBehavior()).setTexturePosition(8, 6).setItemName("saddle");
    public static Item IronDoor = new Item(74).SetBehavior(new DoorBehavior(Material.Metal)).setTexturePosition(12, 2).setItemName("doorIron");
    public static Item Redstone = new Item(75).SetBehavior(new RedstoneBehavior()).setTexturePosition(8, 3).setItemName("redstone");
    public static Item Snowball = new Item(76).SetBehavior(new ThrowableBehavior(16, (w, p) => new EntitySnowball(w, p))).setTexturePosition(14, 0).setItemName("snowball");
    public static Item Boat = new Item(77).SetBehavior(new BoatBehavior()).setTexturePosition(8, 8).setItemName("boat");
    public static Item Leather = new Item(78).setTexturePosition(7, 6).setItemName("Leather");
    public static Item MilkBucket = new Item(79).SetBehavior(new BucketBehavior(-1)).setTexturePosition(13, 4).setItemName("milk").setCraftingReturnItem(Bucket);
    public static Item Brick = new Item(80).setTexturePosition(6, 1).setItemName("brick");
    public static Item Clay = new Item(81).setTexturePosition(9, 3).setItemName("clay");
    public static Item SugarCane = new Item(82).SetBehavior(new ReedBehavior(Block.SugarCane)).setTexturePosition(11, 1).setItemName("reeds");
    public static Item Paper = new Item(83).setTexturePosition(10, 3).setItemName("paper");
    public static Item Book = new Item(84).setTexturePosition(11, 3).setItemName("book");
    public static Item Slimeball = new Item(85).setTexturePosition(14, 1).setItemName("slimeball");
    public static Item ChestMinecart = new Item(86).SetBehavior(new MinecartBehavior(1)).setTexturePosition(7, 9).setItemName("minecartChest");
    public static Item FurnaceMinecart = new Item(87).SetBehavior(new MinecartBehavior(2)).setTexturePosition(7, 10).setItemName("minecartFurnace");
    public static Item Egg = new Item(88).SetBehavior(new ThrowableBehavior(16, (w, p) => new EntityEgg(w, p))).setTexturePosition(12, 0).setItemName("egg");
    public static Item Compass = new Item(89).setTexturePosition(6, 3).setItemName("compass");
    public static Item FishingRod = new Item(90).SetBehavior(new FishingRodBehavior()).setTexturePosition(5, 4).setItemName("fishingRod");
    public static Item Clock = new Item(91).setTexturePosition(6, 4).setItemName("clock");
    public static Item GlowstoneDust = new Item(92).setTexturePosition(9, 4).setItemName("yellowDust");
    public static Item RawFish = new Item(93).SetBehavior(new FoodBehavior(2, false)).setTexturePosition(9, 5).setItemName("fishRaw");
    public static Item CookedFish = new Item(94).SetBehavior(new FoodBehavior(5, false)).setTexturePosition(10, 5).setItemName("fishCooked");
    public static Item Dye = new Item(95).SetBehavior(new DyeBehavior()).setTexturePosition(14, 4).setItemName("dyePowder");
    public static Item Bone = new Item(96).setTexturePosition(12, 1).setItemName("bone").setHandheld();
    public static Item Sugar = new Item(97).setTexturePosition(13, 0).setItemName("sugar").setHandheld();
    public static Item Cake = new Item(98).SetBehavior(new ReedBehavior(Block.Cake)).setMaxCount(1).setTexturePosition(13, 1).setItemName("cake");
    public static Item Bed = new Item(99).SetBehavior(new BedBehavior()).setTexturePosition(13, 2).setItemName("bed");
    public static Item Repeater = new Item(100).SetBehavior(new ReedBehavior(Block.Repeater)).setTexturePosition(6, 5).setItemName("diode");
    public static Item Cookie = new Item(101).SetBehavior(new FoodBehavior(1, false, 8)).setTexturePosition(12, 5).setItemName("cookie");
    public static Item Map = new Item(102).SetBehavior(new MapBehavior()).setTexturePosition(12, 3).setItemName("map");
    public static Item Shears = new Item(103).SetBehavior(new ShearsBehavior()).setTexturePosition(13, 5).setItemName("shears");
    public static Item RecordThirteen = new Item(2000).SetBehavior(new RecordBehavior("13")).setTexturePosition(0, 15).setItemName("record");
    public static Item RecordCat = new Item(2001).SetBehavior(new RecordBehavior("cat")).setTexturePosition(1, 15).setItemName("record");
    private readonly ILogger<Item> _logger = Log.Instance.For<Item>();

    public readonly int id;
    private IItemBehavior? _behavior;
    private Item craftingReturnItem;
    internal bool handheld;
    internal bool hasSubtypes;
    public int maxCount = 64;
    private int maxDamage;
    internal int textureId;
    private string translationKey;

    static Item() => Stats.Stats.InitializeExtendedItemStats();

    protected Item(int id)
    {
        this.id = 256 + id;
        if (ITEMS[256 + id] != null)
        {
            _logger.LogInformation($"CONFLICT @ {id}");
        }

        ITEMS[256 + id] = this;
    }

    public virtual IReadOnlyList<string> GetItemAlias
        => _behavior?.GetItemAliases(this) ?? [];

    public Item SetBehavior(IItemBehavior behavior)
    {
        _behavior = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior
        => _behavior as TBehavior;

    public Item setTextureId(int textureId)
    {
        this.textureId = textureId;
        return this;
    }

    public Item setMaxCount(int maxCount)
    {
        this.maxCount = maxCount;
        return this;
    }

    public Item setTexturePosition(int x, int y)
    {
        textureId = x + y * 16;
        return this;
    }

    public virtual int getTextureId(int damage)
        => _behavior?.GetTextureId(this, damage) ?? textureId;

    public int getTextureId(ItemStack stack)
        => getTextureId(stack.getDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
        => _behavior?.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta) ?? false;

    public virtual float getMiningSpeedMultiplier(ItemStack itemStack, Block block)
        => _behavior?.GetMiningSpeedMultiplier(this, itemStack, block) ?? 1.0F;

    public virtual ItemStack use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
        => _behavior?.Use(this, itemStack, world, entityPlayer) ?? itemStack;

    public int getMaxCount()
        => maxCount;

    public virtual int getPlacementMetadata(int meta)
        => 0;

    public bool getHasSubtypes()
        => hasSubtypes;

    internal Item setHasSubtypes(bool has)
    {
        hasSubtypes = has;
        return this;
    }

    public int getMaxDamage()
        => maxDamage;

    internal Item setMaxDamage(int dmg)
    {
        maxDamage = dmg;
        return this;
    }

    public bool isDamagable()
        => maxDamage > 0 && !hasSubtypes;

    public virtual bool postHit(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)
        => _behavior?.PostHit(this, itemStack, entityLiving, entityPlayer) ?? false;

    public virtual bool postMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving)
        => _behavior?.PostMine(this, itemStack, blockId, x, y, z, entityLiving) ?? false;

    public virtual int getAttackDamage(Entity entity)
        => _behavior?.GetAttackDamage(this, entity) ?? 1;

    public virtual bool isSuitableFor(Block block)
        => _behavior?.IsSuitableFor(this, block) ?? false;

    public virtual void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)
        => _behavior?.UseOnEntity(this, itemStack, entityLiving, entityPlayer);

    public Item setHandheld()
    {
        handheld = true;
        return this;
    }

    public virtual bool isHandheld()
        => _behavior?.IsHandheld(this) ?? handheld;

    public virtual bool isHandheldRod()
        => _behavior?.IsHandheldRod(this) ?? false;

    public Item setItemName(string name)
    {
        translationKey = "item." + name;
        return this;
    }

    public virtual string getItemName()
        => translationKey;

    public virtual string getItemNameIS(ItemStack itemStack)
        => _behavior?.GetItemNameIS(this, itemStack) ?? translationKey;

    public Item setCraftingReturnItem(Item item)
    {
        if (maxCount > 1)
        {
            throw new ArgumentException("Max stack size must be 1 for items with crafting results");
        }

        craftingReturnItem = item;
        return this;
    }

    public Item getContainerItem()
        => craftingReturnItem;

    public bool hasContainerItem()
        => craftingReturnItem != null;

    public string getStatName()
        => StatCollector.TranslateToLocal(getItemName() + ".name");

    public virtual int getColorMultiplier(int color)
        => 0xFFFFFF;

    public virtual void inventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
        => _behavior?.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);

    public virtual void onCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
        => _behavior?.OnCraft(this, itemStack, world, entityPlayer);

    public virtual bool isNetworkSynced()
        => _behavior?.IsNetworkSynced(this) ?? false;

    public virtual Packet? getUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player)
        => _behavior?.GetUpdatePacket(this, stack, world, player);

    private static Func<Block, bool> PickaxeSuitableFor(ToolMaterial material) => block =>
    {
        if (block == Block.Obsidian)
        {
            return material.getHarvestLevel() == 3;
        }

        if (block == Block.DiamondBlock || block == Block.DiamondOre)
        {
            return material.getHarvestLevel() >= 2;
        }

        if (block == Block.GoldBlock || block == Block.GoldOre)
        {
            return material.getHarvestLevel() >= 2;
        }

        if (block == Block.IronBlock || block == Block.IronOre)
        {
            return material.getHarvestLevel() >= 1;
        }

        if (block == Block.LapisBlock || block == Block.LapisOre)
        {
            return material.getHarvestLevel() >= 1;
        }

        if (block == Block.RedstoneOre || block == Block.LitRedstoneOre)
        {
            return material.getHarvestLevel() >= 2;
        }

        return block.material == Material.Stone || block.material == Material.Metal;
    };
}
