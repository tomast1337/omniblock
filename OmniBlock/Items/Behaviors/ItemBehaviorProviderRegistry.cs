using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Items.Behaviors;

/// <summary>Built-in C# item behavior providers. Future mod providers implement the same boundary.</summary>
public sealed class ItemBehaviorProviderRegistry : IItemBehaviorProviderRegistry
{
    public IItemBehavior Build(ItemBehaviorDefinition definition, ItemBuildContext context) => definition switch
    {
        FoodBehaviorDefinition food => new FoodBehavior(food.HealAmount, food.IsMeat,
            food.ReturnItem is null ? null : context.ResolveItem(ResourceLocation.Parse(food.ReturnItem))),
        ToolBehaviorDefinition tool => BuildTool(tool, context),
        SwordBehaviorDefinition sword => new SwordBehavior(context.ResolveToolMaterial(ResourceLocation.Parse(sword.Material))),
        HoeBehaviorDefinition hoe => new HoeBehavior(context.ResolveToolMaterial(ResourceLocation.Parse(hoe.Material))),
        ArmorBehaviorDefinition armor => new ArmorBehavior(context.ResolveArmorMaterial(ResourceLocation.Parse(armor.Material)), armor.Slot),
        ShearsBehaviorDefinition => new ShearsBehavior(),
        FlintAndSteelBehaviorDefinition => new FlintAndSteelBehavior(),
        FishingRodBehaviorDefinition rod => new FishingRodBehavior(context.ResolveItemTexture(rod.Cast)),
        BowBehaviorDefinition => new BowBehavior(),
        BucketBehaviorDefinition bucket => new BucketBehavior(() => bucket.Liquid switch
        {
            "water" => context.ResolveBlock("omniblock:flowing_water").Id,
            "lava" => context.ResolveBlock("omniblock:flowing_lava").Id,
            "milk" => -1,
            _ => 0
        }),
        MinecartBehaviorDefinition cart => new MinecartBehavior(cart.CartType),
        BoatBehaviorDefinition => new BoatBehavior(),
        BedBehaviorDefinition => new BedBehavior(),
        DoorBehaviorDefinition door => new DoorBehavior(door.DoorMaterial == "iron"
            ? context.ResolveBlockMaterial("omniblock:metal")
            : context.ResolveBlockMaterial("omniblock:wood")),
        SeedsBehaviorDefinition seeds => new SeedsBehavior(() => context.ResolveBlock(ParseRequired(seeds.PlacesBlock, "PlacesBlock")).Id),
        PlaceBlockBehaviorDefinition place => new PlaceBlockBehavior(() => context.ResolveBlock(ParseRequired(place.PlacesBlock, "PlacesBlock"))),
        ThrowableBehaviorDefinition thrown => new ThrowableBehavior((world, _) =>
            context.ResolveEntityType(ResourceLocation.Parse(thrown.ProjectileType)).Create(world)),
        DyeBehaviorDefinition dye => new DyeBehavior([.. dye.Textures.Select(context.ResolveItemTexture)]),
        CoalBehaviorDefinition => new CoalBehavior(),
        RecordBehaviorDefinition record => new RecordBehavior(record.RecordName),
        RedstoneBehaviorDefinition => new RedstoneBehavior(),
        SignBehaviorDefinition => new SignBehavior(),
        PaintingBehaviorDefinition => new PaintingBehavior(),
        SaddleBehaviorDefinition => new SaddleBehavior(),
        MapBehaviorDefinition => new MapBehavior(),
        _ => throw new ArgumentException($"Unknown item behavior definition '{definition.GetType().FullName}'.", nameof(definition))
    };

    private static IItemBehavior BuildTool(ToolBehaviorDefinition definition, ItemBuildContext context)
    {
        ToolMaterial material = context.ResolveToolMaterial(ResourceLocation.Parse(definition.Material));
        return definition.ToolType switch
        {
            "pickaxe" => new ToolBehavior(material, 2, () => Item.s_pickaxeBlocks, Item.PickaxeSuitableFor(material)),
            "axe" => new ToolBehavior(material, 3, () => Item.s_axeBlocks),
            _ => new ToolBehavior(material, 1, () => Item.s_spadeBlocks,
                block => block == context.ResolveBlock("omniblock:snow")
                         || block == context.ResolveBlock("omniblock:snow_block"))
        };
    }

    private static ResourceLocation ParseRequired(string? value, string property) =>
        ResourceLocation.Parse(value ?? throw new ArgumentException($"Item behavior requires '{property}'."));
}
