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

    internal static readonly Block[] s_spadeBlocks = [Block.GrassBlock, Block.Dirt, Block.Sand, Block.Gravel, Block.Snow, Block.SnowBlock, Block.Clay, Block.Farmland];

    internal static readonly Block[] s_pickaxeBlocks =
    [
        Block.Cobblestone, Block.DoubleSlab, Block.Slab, Block.Stone, Block.Sandstone, Block.MossyCobblestone, Block.IronOre, Block.IronBlock, Block.CoalOre, Block.GoldBlock, Block.GoldOre, Block.DiamondOre, Block.DiamondBlock, Block.Ice,
        Block.Netherrack, Block.LapisOre, Block.LapisBlock, Block.RedstoneOre, Block.CobblestoneStairs
    ];

    internal static readonly Block[] s_axeBlocks = [Block.Planks, Block.Bookshelf, Block.Log, Block.Chest, Block.CraftingTable, Block.WoodenStairs, Block.Ladder, Block.Trapdoor, Block.Fence];

    public static Item IronShovel = ItemFactory.Create(new ItemDefinition {    ProtocolId = 256,    TranslationKey = "shovelIron",    TextureX = 2,    TextureY = 5,    Behavior = new ToolBehaviorDefinition { ToolType = "shovel", Material = "iron" }});
    public static Item IronPickaxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 257,    TranslationKey = "pickaxeIron",    TextureX = 2,    TextureY = 6,    Behavior = new ToolBehaviorDefinition { ToolType = "pickaxe", Material = "iron" }});
    public static Item IronAxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 258,    TranslationKey = "hatchetIron",    TextureX = 2,    TextureY = 7,    Behavior = new ToolBehaviorDefinition { ToolType = "axe", Material = "iron" }});
    public static Item FlintAndSteel = ItemFactory.Create(new ItemDefinition{    ProtocolId = 259,    TranslationKey = "flintAndSteel",    TextureX = 5,    TextureY = 0,    Behavior = new FlintAndSteelBehaviorDefinition()});
    public static Item Apple = ItemFactory.Create(new ItemDefinition{    ProtocolId = 260,    TranslationKey = "apple",    TextureX = 10,    TextureY = 0,    Behavior = new FoodBehaviorDefinition { HealAmount = 4 }});
    public static Item BOW = ItemFactory.Create(new ItemDefinition {     ProtocolId = 261,     TranslationKey = "bow",     TextureX = 5,     TextureY = 1,     Behavior = new BowBehaviorDefinition() });
    public static Item ARROW = ItemFactory.Create(new ItemDefinition {     ProtocolId = 262,     TranslationKey = "arrow",     TextureX = 5,     TextureY = 2 });
    public static Item Coal = ItemFactory.Create(new ItemDefinition {     ProtocolId = 263,     TranslationKey = "coal",     TextureX = 7,     TextureY = 0,     Behavior = new CoalBehaviorDefinition() });
    public static Item Diamond = ItemFactory.Create(new ItemDefinition {     ProtocolId = 264,     TranslationKey = "emerald",     TextureX = 7,     TextureY = 3 });
    public static Item IronIngot = ItemFactory.Create(new ItemDefinition {     ProtocolId = 265,     TranslationKey = "ingotIron",     TextureX = 7,     TextureY = 1 });
    public static Item GoldIngot = ItemFactory.Create(new ItemDefinition {     ProtocolId = 266,     TranslationKey = "ingotGold",     TextureX = 7,     TextureY = 2 });
    public static Item IronSword = ItemFactory.Create(new ItemDefinition{    ProtocolId = 267,    TranslationKey = "swordIron",    TextureX = 2,    TextureY = 4,    Behavior = new SwordBehaviorDefinition { Material = "iron" }});
    public static Item WoodenSword = ItemFactory.Create(new ItemDefinition{    ProtocolId = 268,    TranslationKey = "swordWood",    TextureX = 0,    TextureY = 4,    Behavior = new SwordBehaviorDefinition { Material = "wood" }});
    public static Item WoodenShovel = ItemFactory.Create(new ItemDefinition {     ProtocolId = 269,     TranslationKey = "shovelWood",     TextureX = 0,     TextureY = 5,     Behavior = new ToolBehaviorDefinition { ToolType = "shovel", Material = "wood" } });
    public static Item WoodenPickaxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 270,    TranslationKey = "pickaxeWood",    TextureX = 0,    TextureY = 6,    Behavior = new ToolBehaviorDefinition { ToolType = "pickaxe", Material = "wood" }});
    public static Item WoodenAxe = ItemFactory.Create(new ItemDefinition {     ProtocolId = 271,     TranslationKey = "hatchetWood",     TextureX = 0,     TextureY = 7,     Behavior = new ToolBehaviorDefinition { ToolType = "axe", Material = "wood" } });
    public static Item StoneSword = ItemFactory.Create(new ItemDefinition {     ProtocolId = 272,     TranslationKey = "swordStone",     TextureX = 1,     TextureY = 4,     Behavior = new SwordBehaviorDefinition { Material = "stone" } });
    public static Item StoneShovel = ItemFactory.Create(new ItemDefinition{    ProtocolId = 273,    TranslationKey = "shovelStone",    TextureX = 1,    TextureY = 5,    Behavior = new ToolBehaviorDefinition { ToolType = "shovel", Material = "stone" }});
    public static Item StonePickaxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 274,    TranslationKey = "pickaxeStone",    TextureX = 1,    TextureY = 6,    Behavior = new ToolBehaviorDefinition { ToolType = "pickaxe", Material = "stone" }});
    public static Item StoneAxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 275,    TranslationKey = "hatchetStone",    TextureX = 1,    TextureY = 7,    Behavior = new ToolBehaviorDefinition { ToolType = "axe", Material = "stone" }});
    public static Item DiamondSword = ItemFactory.Create(new ItemDefinition{    ProtocolId = 276,    TranslationKey = "swordDiamond",    TextureX = 3,    TextureY = 4,    Behavior = new SwordBehaviorDefinition { Material = "emerald" }});
    public static Item DiamondShovel = ItemFactory.Create(new ItemDefinition{    ProtocolId = 277,    TranslationKey = "shovelDiamond",    TextureX = 3,    TextureY = 5,    Behavior = new ToolBehaviorDefinition { ToolType = "shovel", Material = "emerald" }});
    public static Item DiamondPickaxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 278,    TranslationKey = "pickaxeDiamond",    TextureX = 3,    TextureY = 6,    Behavior = new ToolBehaviorDefinition { ToolType = "pickaxe", Material = "emerald" }});
    public static Item DiamondAxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 279,    TranslationKey = "hatchetDiamond",    TextureX = 3,    TextureY = 7,    Behavior = new ToolBehaviorDefinition { ToolType = "axe", Material = "emerald" }});
    public static Item Stick = ItemFactory.Create(new ItemDefinition{    ProtocolId = 280,    TranslationKey = "stick",    TextureX = 5,    TextureY = 3,    Handheld = true});
    public static Item Bowl = ItemFactory.Create(new ItemDefinition {     ProtocolId = 281,     TranslationKey = "bowl",     TextureX = 7,     TextureY = 4 });
    public static Item MushroomStew = ItemFactory.Create(new ItemDefinition{    ProtocolId = 282,    TranslationKey = "mushroomStew",    TextureX = 8,    TextureY = 4,    Behavior = new FoodBehaviorDefinition { HealAmount = 10, ReturnItemProtocolId = 281 }});
    public static Item GoldenSword = ItemFactory.Create(new ItemDefinition{    ProtocolId = 283,    TranslationKey = "swordGold",    TextureX = 4,    TextureY = 4,    Behavior = new SwordBehaviorDefinition { Material = "gold" }});
    public static Item GoldenShovel = ItemFactory.Create(new ItemDefinition{    ProtocolId = 284,    TranslationKey = "shovelGold",    TextureX = 4,    TextureY = 5,    Behavior = new ToolBehaviorDefinition { ToolType = "shovel", Material = "gold" }});
    public static Item GoldenPickaxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 285,    TranslationKey = "pickaxeGold",    TextureX = 4,    TextureY = 6,    Behavior = new ToolBehaviorDefinition { ToolType = "pickaxe", Material = "gold" }});
    public static Item GoldenAxe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 286,    TranslationKey = "hatchetGold",    TextureX = 4,    TextureY = 7,    Behavior = new ToolBehaviorDefinition { ToolType = "axe", Material = "gold" }});
    public static Item String = ItemFactory.Create(new ItemDefinition{    ProtocolId = 287,    TranslationKey = "string",    TextureX = 8,    TextureY = 0});
    public static Item Feather = ItemFactory.Create(new ItemDefinition{    ProtocolId = 288,    TranslationKey = "feather",    TextureX = 8,    TextureY = 1});
    public static Item Gunpowder = ItemFactory.Create(new ItemDefinition{    ProtocolId = 289,    TranslationKey = "sulphur",    TextureX = 8,    TextureY = 2});
    public static Item WoodenHoe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 290,    TranslationKey = "hoeWood",    TextureX = 0,    TextureY = 8,    Behavior = new HoeBehaviorDefinition { Material = "wood" }});
    public static Item StoneHoe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 291,    TranslationKey = "hoeStone",    TextureX = 1,    TextureY = 8,    Behavior = new HoeBehaviorDefinition { Material = "stone" }});
    public static Item IronHoe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 292,    TranslationKey = "hoeIron",    TextureX = 2,    TextureY = 8,    Behavior = new HoeBehaviorDefinition { Material = "iron" }});
    public static Item DiamondHoe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 293,    TranslationKey = "hoeDiamond",    TextureX = 3,    TextureY = 8,    Behavior = new HoeBehaviorDefinition { Material = "emerald" }});
    public static Item GoldenHoe = ItemFactory.Create(new ItemDefinition{    ProtocolId = 294,    TranslationKey = "hoeGold",    TextureX = 4,    TextureY = 8,    Behavior = new HoeBehaviorDefinition { Material = "gold" }});
    public static Item Seeds = ItemFactory.Create(new ItemDefinition{    ProtocolId = 295,    TranslationKey = "seeds",    TextureX = 9,    TextureY = 0,    Behavior = new SeedsBehaviorDefinition { CropBlockId = Block.Wheat.id }});
    public static Item Wheat = ItemFactory.Create(new ItemDefinition{    ProtocolId = 296,    TranslationKey = "wheat",    TextureX = 9,    TextureY = 1});
    public static Item Bread = ItemFactory.Create(new ItemDefinition{    ProtocolId = 297,    TranslationKey = "bread",    TextureX = 9,    TextureY = 2,    Behavior = new FoodBehaviorDefinition { HealAmount = 5 }});
    public static Item LeatherHelmet = ItemFactory.Create(new ItemDefinition{    ProtocolId = 298,    TranslationKey = "helmetCloth",    TextureX = 0,    TextureY = 0,    Behavior = new ArmorBehaviorDefinition { Material = "leather", Slot = 0 }});
    public static Item LeatherChestplate = ItemFactory.Create(new ItemDefinition{    ProtocolId = 299,    TranslationKey = "chestplateCloth",    TextureX = 0,    TextureY = 1,    Behavior = new ArmorBehaviorDefinition { Material = "leather", Slot = 1 }});
    public static Item LeatherLeggings = ItemFactory.Create(new ItemDefinition{    ProtocolId = 300,    TranslationKey = "leggingsCloth",    TextureX = 0,    TextureY = 2,    Behavior = new ArmorBehaviorDefinition { Material = "leather", Slot = 2 }});
    public static Item LeatherBoots = ItemFactory.Create(new ItemDefinition{    ProtocolId = 301,    TranslationKey = "bootsCloth",    TextureX = 0,    TextureY = 3,    Behavior = new ArmorBehaviorDefinition { Material = "leather", Slot = 3 }});
    public static Item ChainHelmet = ItemFactory.Create(new ItemDefinition{    ProtocolId = 302,    TranslationKey = "helmetChain",    TextureX = 1,    TextureY = 0,    Behavior = new ArmorBehaviorDefinition { Material = "chain", Slot = 0 }});
    public static Item ChainChestplate = ItemFactory.Create(new ItemDefinition{    ProtocolId = 303,    TranslationKey = "chestplateChain",    TextureX = 1,    TextureY = 1,    Behavior = new ArmorBehaviorDefinition { Material = "chain", Slot = 1 }});
    public static Item ChainLeggings = ItemFactory.Create(new ItemDefinition {     ProtocolId = 304,     TranslationKey = "leggingsChain",     TextureX = 1,     TextureY = 2,     Behavior = new ArmorBehaviorDefinition { Material = "chain", Slot = 2 } });
    public static Item ChainBoots = ItemFactory.Create(new ItemDefinition{    ProtocolId = 305,    TranslationKey = "bootsChain",    TextureX = 1,    TextureY = 3,    Behavior = new ArmorBehaviorDefinition { Material = "chain", Slot = 3 }});
    public static Item IronHelmet = ItemFactory.Create(new ItemDefinition{    ProtocolId = 306,    TranslationKey = "helmetIron",    TextureX = 2,    TextureY = 0,    Behavior = new ArmorBehaviorDefinition { Material = "iron", Slot = 0 }});
    public static Item IronChestplate = ItemFactory.Create(new ItemDefinition{    ProtocolId = 307,    TranslationKey = "chestplateIron",    TextureX = 2,    TextureY = 1,    Behavior = new ArmorBehaviorDefinition { Material = "iron", Slot = 1 }});
    public static Item IronLeggings = ItemFactory.Create(new ItemDefinition{    ProtocolId = 308,    TranslationKey = "leggingsIron",    TextureX = 2,    TextureY = 2,    Behavior = new ArmorBehaviorDefinition { Material = "iron", Slot = 2 }});
    public static Item IronBoots = ItemFactory.Create(new ItemDefinition{    ProtocolId = 309,    TranslationKey = "bootsIron",    TextureX = 2,    TextureY = 3,    Behavior = new ArmorBehaviorDefinition { Material = "iron", Slot = 3 }});
    public static Item DiamondHelmet = ItemFactory.Create(new ItemDefinition{    ProtocolId = 310,    TranslationKey = "helmetDiamond",    TextureX = 3,    TextureY = 0,    Behavior = new ArmorBehaviorDefinition { Material = "diamond", Slot = 0 }});
    public static Item DiamondChestplate = ItemFactory.Create(new ItemDefinition{    ProtocolId = 311,    TranslationKey = "chestplateDiamond",    TextureX = 3,    TextureY = 1,    Behavior = new ArmorBehaviorDefinition { Material = "diamond", Slot = 1 }});
    public static Item DiamondLeggings = ItemFactory.Create(new ItemDefinition{    ProtocolId = 312,    TranslationKey = "leggingsDiamond",    TextureX = 3,    TextureY = 2,    Behavior = new ArmorBehaviorDefinition { Material = "diamond", Slot = 2 }});
    public static Item DiamondBoots = ItemFactory.Create(new ItemDefinition{    ProtocolId = 313,    TranslationKey = "bootsDiamond",    TextureX = 3,    TextureY = 3,    Behavior = new ArmorBehaviorDefinition { Material = "diamond", Slot = 3 }});
    public static Item GoldenHelmet = ItemFactory.Create(new ItemDefinition{    ProtocolId = 314,    TranslationKey = "helmetGold",    TextureX = 4,    TextureY = 0,    Behavior = new ArmorBehaviorDefinition { Material = "gold", Slot = 0 }});
    public static Item GoldenChestplate = ItemFactory.Create(new ItemDefinition{    ProtocolId = 315,    TranslationKey = "chestplateGold",    TextureX = 4,    TextureY = 1,    Behavior = new ArmorBehaviorDefinition { Material = "gold", Slot = 1 }});
    public static Item GoldenLeggings = ItemFactory.Create(new ItemDefinition{    ProtocolId = 316,    TranslationKey = "leggingsGold",    TextureX = 4,    TextureY = 2,    Behavior = new ArmorBehaviorDefinition { Material = "gold", Slot = 2 }});
    public static Item GoldenBoots = ItemFactory.Create(new ItemDefinition{    ProtocolId = 317,    TranslationKey = "bootsGold",    TextureX = 4,    TextureY = 3,    Behavior = new ArmorBehaviorDefinition { Material = "gold", Slot = 3 }});
    public static Item Flint = ItemFactory.Create(new ItemDefinition{    ProtocolId = 318,    TranslationKey = "flint",    TextureX = 6,    TextureY = 0});
    public static Item RawPorkchop = ItemFactory.Create(new ItemDefinition{    ProtocolId = 319,    TranslationKey = "porkchopRaw",    TextureX = 7,    TextureY = 5,    Behavior = new FoodBehaviorDefinition { HealAmount = 3, IsWolfsFavoriteMeat = true }});
    public static Item CookedPorkchop = ItemFactory.Create(new ItemDefinition{    ProtocolId = 320,    TranslationKey = "porkchopCooked",    TextureX = 8,    TextureY = 5,    Behavior = new FoodBehaviorDefinition { HealAmount = 8, IsWolfsFavoriteMeat = true }});
    public static Item Painting = ItemFactory.Create(new ItemDefinition{    ProtocolId = 321,    TranslationKey = "painting",    TextureX = 10,    TextureY = 1,    Behavior = new PaintingBehaviorDefinition()});
    public static Item GoldenApple = ItemFactory.Create(new ItemDefinition{    ProtocolId = 322,    TranslationKey = "appleGold",    TextureX = 11,    TextureY = 0,    Behavior = new FoodBehaviorDefinition { HealAmount = 42 }});
    public static Item Sign = ItemFactory.Create(new ItemDefinition{    ProtocolId = 323,    TranslationKey = "sign",    TextureX = 10,    TextureY = 2,    Behavior = new SignBehaviorDefinition()});
    public static Item WoodenDoor = ItemFactory.Create(new ItemDefinition{    ProtocolId = 324,    TranslationKey = "doorWood",    TextureX = 11,    TextureY = 2,    Behavior = new DoorBehaviorDefinition { DoorMaterial = "wood" }});
    public static Item Bucket = ItemFactory.Create(new ItemDefinition{    ProtocolId = 325,    TranslationKey = "bucket",    TextureX = 10,    TextureY = 4,    Behavior = new BucketBehaviorDefinition { Liquid = "empty" }});
    public static Item WaterBucket = ItemFactory.Create(new ItemDefinition{    ProtocolId = 326,    TranslationKey = "bucketWater",    TextureX = 11,    TextureY = 4,    Behavior = new BucketBehaviorDefinition { Liquid = "water" },    CraftingReturnItemProtocolId = 325});
    public static Item LavaBucket = ItemFactory.Create(new ItemDefinition{    ProtocolId = 327,    TranslationKey = "bucketLava",    TextureX = 12,    TextureY = 4,    Behavior = new BucketBehaviorDefinition { Liquid = "lava" },    CraftingReturnItemProtocolId = 325});
    public static Item Minecart = ItemFactory.Create(new ItemDefinition{    ProtocolId = 328,    TranslationKey = "minecart",    TextureX = 7,    TextureY = 8,    Behavior = new MinecartBehaviorDefinition { CartType = 0 }});
    public static Item Saddle = ItemFactory.Create(new ItemDefinition{    ProtocolId = 329,    TranslationKey = "saddle",    TextureX = 8,    TextureY = 6,    Behavior = new SaddleBehaviorDefinition()});
    public static Item IronDoor = ItemFactory.Create(new ItemDefinition{    ProtocolId = 330,    TranslationKey = "doorIron",    TextureX = 12,    TextureY = 2,    Behavior = new DoorBehaviorDefinition { DoorMaterial = "iron" }});
    public static Item Redstone = ItemFactory.Create(new ItemDefinition{    ProtocolId = 331,    TranslationKey = "redstone",    TextureX = 8,    TextureY = 3,    Behavior = new RedstoneBehaviorDefinition()});
    public static Item Snowball = ItemFactory.Create(new ItemDefinition{    ProtocolId = 332,    TranslationKey = "snowball",    TextureX = 14,    TextureY = 0,    Behavior = new ThrowableBehaviorDefinition { MaxCount = 16, ProjectileType = "snowball" }});
    public static Item Boat = ItemFactory.Create(new ItemDefinition{    ProtocolId = 333,    TranslationKey = "boat",    TextureX = 8,    TextureY = 8,    Behavior = new BoatBehaviorDefinition()});
    public static Item Leather = ItemFactory.Create(new ItemDefinition{    ProtocolId = 334,    TranslationKey = "Leather",    TextureX = 7,    TextureY = 6});
    public static Item MilkBucket = ItemFactory.Create(new ItemDefinition{    ProtocolId = 335,    TranslationKey = "milk",    TextureX = 13,    TextureY = 4,    Behavior = new BucketBehaviorDefinition { Liquid = "milk" },    CraftingReturnItemProtocolId = 325});
    public static Item Brick = ItemFactory.Create(new ItemDefinition{    ProtocolId = 336,    TranslationKey = "brick",    TextureX = 6,    TextureY = 1});
    public static Item Clay = ItemFactory.Create(new ItemDefinition{    ProtocolId = 337,    TranslationKey = "clay",    TextureX = 9,    TextureY = 3});
    public static Item SugarCane = ItemFactory.Create(new ItemDefinition{    ProtocolId = 338,    TranslationKey = "reeds",    TextureX = 11,    TextureY = 1,    Behavior = new ReedBehaviorDefinition { BlockId = Block.SugarCane.id }});
    public static Item Paper = ItemFactory.Create(new ItemDefinition{    ProtocolId = 339,    TranslationKey = "paper",    TextureX = 10,    TextureY = 3});
    public static Item Book = ItemFactory.Create(new ItemDefinition{    ProtocolId = 340,    TranslationKey = "book",    TextureX = 11,    TextureY = 3});
    public static Item Slimeball = ItemFactory.Create(new ItemDefinition {     ProtocolId = 341,     TranslationKey = "slimeball",     TextureX = 14,     TextureY = 1 });
    public static Item ChestMinecart = ItemFactory.Create(new ItemDefinition{    ProtocolId = 342,    TranslationKey = "minecartChest",    TextureX = 7,    TextureY = 9,    Behavior = new MinecartBehaviorDefinition { CartType = 1 }});
    public static Item FurnaceMinecart = ItemFactory.Create(new ItemDefinition{    ProtocolId = 343,    TranslationKey = "minecartFurnace",    TextureX = 7,    TextureY = 10,    Behavior = new MinecartBehaviorDefinition { CartType = 2 }});
    public static Item Egg = ItemFactory.Create(new ItemDefinition {     ProtocolId = 344,     TranslationKey = "egg",     TextureX = 12,     TextureY = 0,     Behavior = new ThrowableBehaviorDefinition { MaxCount = 16, ProjectileType = "egg" } });
    public static Item Compass = ItemFactory.Create(new ItemDefinition{    ProtocolId = 345,    TranslationKey = "compass",    TextureX = 6,    TextureY = 3});
    public static Item FishingRod = ItemFactory.Create(new ItemDefinition{    ProtocolId = 346,    TranslationKey = "fishingRod",    TextureX = 5,    TextureY = 4,    Behavior = new FishingRodBehaviorDefinition()});
    public static Item Clock = ItemFactory.Create(new ItemDefinition {     ProtocolId = 347,     TranslationKey = "clock",     TextureX = 6,     TextureY = 4 });
    public static Item GlowstoneDust = ItemFactory.Create(new ItemDefinition {     ProtocolId = 348,     TranslationKey = "yellowDust",     TextureX = 9,     TextureY = 4 });
    public static Item RawFish = ItemFactory.Create(new ItemDefinition{    ProtocolId = 349,    TranslationKey = "fishRaw",    TextureX = 9,    TextureY = 5,    Behavior = new FoodBehaviorDefinition { HealAmount = 2 }});
    public static Item CookedFish = ItemFactory.Create(new ItemDefinition {     ProtocolId = 350,     TranslationKey = "fishCooked",     TextureX = 10,     TextureY = 5,     Behavior = new FoodBehaviorDefinition { HealAmount = 5 } });
    public static Item Dye = ItemFactory.Create(new ItemDefinition {     ProtocolId = 351,     TranslationKey = "dyePowder",     TextureX = 14,     TextureY = 4,     Behavior = new DyeBehaviorDefinition() });
    public static Item Bone = ItemFactory.Create(new ItemDefinition{    ProtocolId = 352,    TranslationKey = "bone",    TextureX = 12,    TextureY = 1,    Handheld = true});
    public static Item Sugar = ItemFactory.Create(new ItemDefinition{    ProtocolId = 353,    TranslationKey = "sugar",    TextureX = 13,    TextureY = 0,    Handheld = true});
    public static Item Cake = ItemFactory.Create(new ItemDefinition{    ProtocolId = 354,    TranslationKey = "cake",    TextureX = 13,    TextureY = 1,    MaxStackSize = 1,    Behavior = new ReedBehaviorDefinition { BlockId = Block.Cake.id }});
    public static Item Bed = ItemFactory.Create(new ItemDefinition{    ProtocolId = 355,    TranslationKey = "bed",    TextureX = 13,    TextureY = 2,    Behavior = new BedBehaviorDefinition()});
    public static Item Repeater = ItemFactory.Create(new ItemDefinition{    ProtocolId = 356,    TranslationKey = "diode",    TextureX = 6,    TextureY = 5,    Behavior = new ReedBehaviorDefinition { BlockId = Block.Repeater.id }});
    public static Item Cookie = ItemFactory.Create(new ItemDefinition{    ProtocolId = 357,    TranslationKey = "cookie",    TextureX = 12,    TextureY = 5,    Behavior = new FoodBehaviorDefinition { HealAmount = 1, MaxCount = 8 }});
    public static Item Map = ItemFactory.Create(new ItemDefinition{    ProtocolId = 358,    TranslationKey = "map",    TextureX = 12,    TextureY = 3,    Behavior = new MapBehaviorDefinition()});
    public static Item Shears = ItemFactory.Create(new ItemDefinition{    ProtocolId = 359,    TranslationKey = "shears",    TextureX = 13,    TextureY = 5,    Behavior = new ShearsBehaviorDefinition()});
    public static Item RecordThirteen = ItemFactory.Create(new ItemDefinition{    ProtocolId = 2256,    TranslationKey = "record",    TextureX = 0,    TextureY = 15,    Behavior = new RecordBehaviorDefinition { RecordName = "13" }});
    public static Item RecordCat = ItemFactory.Create(new ItemDefinition{    ProtocolId = 2257,    TranslationKey = "record",    TextureX = 1,    TextureY = 15,    Behavior = new RecordBehaviorDefinition { RecordName = "cat" }});
    
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

    internal Item(int id)
    {
        this.id = 256 + id;
        if (ITEMS[256 + id] != null)
        {
            _logger.LogInformation($"CONFLICT @ {id}");
        }

        ITEMS[256 + id] = this;
    }

    public virtual IReadOnlyList<string> GetItemAlias        => _behavior?.GetItemAliases(this) ?? [];

    public Item SetBehavior(IItemBehavior behavior)
    {
        _behavior = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior        => _behavior as TBehavior;

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

    public virtual int getTextureId(int damage)        => _behavior?.GetTextureId(this, damage) ?? textureId;

    public int getTextureId(ItemStack stack)        => getTextureId(stack.getDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)        => _behavior?.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta) ?? false;

    public virtual float getMiningSpeedMultiplier(ItemStack itemStack, Block block)        => _behavior?.GetMiningSpeedMultiplier(this, itemStack, block) ?? 1.0F;

    public virtual ItemStack use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)        => _behavior?.Use(this, itemStack, world, entityPlayer) ?? itemStack;

    public int getMaxCount()        => maxCount;

    public virtual int getPlacementMetadata(int meta)        => 0;

    public bool getHasSubtypes()        => hasSubtypes;

    internal Item setHasSubtypes(bool has)
    {
        hasSubtypes = has;
        return this;
    }

    public int getMaxDamage()        => maxDamage;

    internal Item setMaxDamage(int dmg)
    {
        maxDamage = dmg;
        return this;
    }

    public bool isDamagable()        => maxDamage > 0 && !hasSubtypes;

    public virtual bool postHit(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)        => _behavior?.PostHit(this, itemStack, entityLiving, entityPlayer) ?? false;

    public virtual bool postMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving)        => _behavior?.PostMine(this, itemStack, blockId, x, y, z, entityLiving) ?? false;

    public virtual int getAttackDamage(Entity entity)        => _behavior?.GetAttackDamage(this, entity) ?? 1;

    public virtual bool isSuitableFor(Block block)        => _behavior?.IsSuitableFor(this, block) ?? false;

    public virtual void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)        => _behavior?.UseOnEntity(this, itemStack, entityLiving, entityPlayer);

    public Item setHandheld()
    {
        handheld = true;
        return this;
    }

    public virtual bool isHandheld()        => _behavior?.IsHandheld(this) ?? handheld;

    public virtual bool isHandheldRod()        => _behavior?.IsHandheldRod(this) ?? false;

    public Item setItemName(string name)
    {
        translationKey = "item." + name;
        return this;
    }

    public virtual string getItemName()        => translationKey;

    public virtual string getItemNameIS(ItemStack itemStack)        => _behavior?.GetItemNameIS(this, itemStack) ?? translationKey;

    public Item setCraftingReturnItem(Item item)
    {
        if (maxCount > 1)
        {
            throw new ArgumentException("Max stack size must be 1 for items with crafting results");
        }

        craftingReturnItem = item;
        return this;
    }

    public Item getContainerItem()        => craftingReturnItem;

    public bool hasContainerItem()        => craftingReturnItem != null;

    public string getStatName()
        => StatCollector.TranslateToLocal(getItemName() + ".name");
    public virtual int getColorMultiplier(int color)        => 0xFFFFFF;

    public virtual void inventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)        => _behavior?.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);

    public virtual void onCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)        => _behavior?.OnCraft(this, itemStack, world, entityPlayer);

    public virtual bool isNetworkSynced()        => _behavior?.IsNetworkSynced(this) ?? false;

    public virtual Packet? getUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player)        => _behavior?.GetUpdatePacket(this, stack, world, player);

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
