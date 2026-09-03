using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Items.Behaviors;

/// <summary>Namespaced item behavior providers. Future native or Luau providers share this boundary.</summary>
public sealed class ItemBehaviorProviderRegistry : IItemBehaviorProviderRegistry
{
    public delegate IItemBehavior BehaviorFactory(JsonElement definition, ItemBuildContext context);
    private readonly FrozenDictionary<ResourceLocation, BehaviorFactory> _factories;

    public ItemBehaviorProviderRegistry() : this(BuiltInFactories()) { }

    public ItemBehaviorProviderRegistry(IEnumerable<KeyValuePair<ResourceLocation, BehaviorFactory>> factories) =>
        _factories = factories.ToFrozenDictionary();

    public IItemBehavior Build(ResourceLocation type, JsonElement definition, in ItemBuildContext context)
    {
        if (!_factories.TryGetValue(type, out BehaviorFactory? factory))
            throw new ArgumentException($"Unknown item behavior type '{type}'.");
        return factory(definition, context);
    }

    private static Dictionary<ResourceLocation, BehaviorFactory> BuiltInFactories() => new()
    {
        [Key("food")] = static (j, c) => new FoodBehavior(Int(j, "HealAmount"), Bool(j, "IsMeat"),
            OptionalString(j, "ReturnItem") is { } key ? c.ResolveItem(ResourceLocation.Parse(key)) : null),
        [Key("tool")] = static (j, c) => BuildTool(j, c),
        [Key("sword")] = static (j, c) => new SwordBehavior(c.ResolveToolMaterial(ResourceLocation.Parse(String(j, "Material")))),
        [Key("hoe")] = static (j, c) => new HoeBehavior(c.ResolveToolMaterial(ResourceLocation.Parse(String(j, "Material")))),
        [Key("armor")] = static (j, c) => new ArmorBehavior(c.ResolveArmorMaterial(ResourceLocation.Parse(String(j, "Material"))), (ArmorSlot)Int(j, "Slot")),
        [Key("shears")] = static (_, _) => new ShearsBehavior(),
        [Key("flint_and_steel")] = static (_, _) => new FlintAndSteelBehavior(),
        [Key("fishing_rod")] = static (j, c) => new FishingRodBehavior(c.ResolveItemTexture(String(j, "Cast"))),
        [Key("bow")] = static (_, c) => new BowBehavior(c.ResolveItem(Key("arrow"))),
        [Key("bucket")] = static (j, c) => BuildBucket(j, c),
        [Key("minecart")] = static (j, _) => new MinecartBehavior(Int(j, "CartType")),
        [Key("boat")] = static (_, _) => new BoatBehavior(), [Key("bed")] = static (_, _) => new BedBehavior(),
        [Key("door")] = static (j, c) => new DoorBehavior(c.ResolveBlockMaterial(String(j, "DoorMaterial", "wood") == "iron" ? "omniblock:metal" : "omniblock:wood")),
        [Key("seeds")] = static (j, c) => BuildSeeds(j, c),
        [Key("place_block")] = static (j, c) => BuildPlaceBlock(j, c),
        [Key("throwable")] = static (j, c) => BuildThrowable(j, c),
        [Key("dye")] = static (j, c) => new DyeBehavior([.. j.GetProperty("Textures").EnumerateArray().Select(value => c.ResolveItemTexture(value.GetString()!))]),
        [Key("coal")] = static (_, _) => new CoalBehavior(), [Key("record")] = static (j, _) => new RecordBehavior(String(j, "RecordName")),
        [Key("redstone")] = static (_, _) => new RedstoneBehavior(), [Key("sign")] = static (_, _) => new SignBehavior(),
        [Key("painting")] = static (_, _) => new PaintingBehavior(), [Key("saddle")] = static (_, _) => new SaddleBehavior(),
        [Key("map")] = static (_, _) => new MapBehavior()
    };

    private static IItemBehavior BuildTool(JsonElement json, ItemBuildContext context)
    {
        ToolMaterial material = context.ResolveToolMaterial(ResourceLocation.Parse(String(json, "Material")));
        return String(json, "Kind", "shovel") switch
        {
            "pickaxe" => BuildPickaxe(material, context),
            "axe" => new ToolBehavior(material, 3, ResolveBlocks(context,
                "planks", "bookshelf", "log", "chest", "crafting_table", "wooden_stairs", "ladder", "trapdoor", "fence")),
            "shovel" => BuildShovel(material, context),
            string kind => throw new ArgumentException($"Unknown tool kind '{kind}'.")
        };
    }

    private static IItemBehavior BuildBucket(JsonElement json, ItemBuildContext context)
    {
        int liquid = String(json, "Liquid", "empty") switch
        {
            "water" => context.ResolveBlock("omniblock:flowing_water").Id,
            "lava" => context.ResolveBlock("omniblock:flowing_lava").Id,
            "milk" => -1,
            _ => 0
        };
        return new BucketBehavior(
            () => liquid,
            context.ResolveItem(Key("bucket")),
            context.ResolveItem(Key("bucket_water")),
            context.ResolveItem(Key("bucket_lava")));
    }

    private static IItemBehavior BuildSeeds(JsonElement json, ItemBuildContext context)
    {
        Block block = context.ResolveBlock(ResourceLocation.Parse(String(json, "PlacesBlock")));
        return new SeedsBehavior(() => block.Id);
    }

    private static IItemBehavior BuildPlaceBlock(JsonElement json, ItemBuildContext context)
    {
        Block block = context.ResolveBlock(ResourceLocation.Parse(String(json, "PlacesBlock")));
        return new PlaceBlockBehavior(() => block);
    }

    private static IItemBehavior BuildThrowable(JsonElement json, ItemBuildContext context)
    {
        ResourceLocation key = ResourceLocation.Parse(String(json, "ProjectileType", "snowball"));
        context.ValidateEntityType(key);
        return new ThrowableBehavior((world, _) => context.ResolveEntityType(key).Create(world));
    }

    private static IItemBehavior BuildShovel(ToolMaterial material, ItemBuildContext context)
    {
        Block snow = context.ResolveBlock("omniblock:snow");
        Block snowBlock = context.ResolveBlock("omniblock:snow_block");
        return new ToolBehavior(material, 1, ResolveBlocks(context,
            "grass_block", "dirt", "sand", "gravel", "snow", "snow_block", "clay", "farmland"),
            block => block == snow || block == snowBlock);
    }

    private static IItemBehavior BuildPickaxe(ToolMaterial material, ItemBuildContext context)
    {
        Block obsidian = context.ResolveBlock("omniblock:obsidian");
        Block[] levelTwo = ResolveBlocks(context, "diamond_block", "diamond_ore", "gold_block", "gold_ore", "redstone_ore", "lit_redstone_ore");
        Block[] levelOne = ResolveBlocks(context, "iron_block", "iron_ore", "lapis_block", "lapis_ore");
        Block[] effective = ResolveBlocks(context, "cobblestone", "double_slab", "slab", "stone", "sandstone",
            "mossy_cobblestone", "iron_ore", "iron_block", "coal_ore", "gold_block", "gold_ore", "diamond_ore",
            "diamond_block", "ice", "netherrack", "lapis_ore", "lapis_block", "redstone_ore", "cobblestone_stairs");
        return new ToolBehavior(material, 2, effective, block =>
            block == obsidian ? material.HarvestLevel == 3 :
            levelTwo.Contains(block) ? material.HarvestLevel >= 2 :
            levelOne.Contains(block) ? material.HarvestLevel >= 1 :
            block.Material == Material.Stone || block.Material == Material.Metal);
    }

    private static Block[] ResolveBlocks(ItemBuildContext context, params string[] names) =>
        [.. names.Select(name => context.ResolveBlock(new ResourceLocation(Namespace.OmniBlock, name)))];

    private static ResourceLocation Key(string path) => new(Namespace.OmniBlock, path);
    private static string String(JsonElement json, string property, string? fallback = null) =>
        json.TryGetProperty(property, out JsonElement value) ? value.GetString()! : fallback ?? throw new ArgumentException($"Item behavior requires '{property}'.");
    private static string? OptionalString(JsonElement json, string property) =>
        json.TryGetProperty(property, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static int Int(JsonElement json, string property) => json.GetProperty(property).GetInt32();
    private static bool Bool(JsonElement json, string property) => json.TryGetProperty(property, out JsonElement value) && value.GetBoolean();
}
