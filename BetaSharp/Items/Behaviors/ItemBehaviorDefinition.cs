using System.Text.Json.Serialization;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Registries;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FoodBehaviorDefinition), "food")]
[JsonDerivedType(typeof(ToolBehaviorDefinition), "tool")]
[JsonDerivedType(typeof(SwordBehaviorDefinition), "sword")]
[JsonDerivedType(typeof(HoeBehaviorDefinition), "hoe")]
[JsonDerivedType(typeof(ArmorBehaviorDefinition), "armor")]
[JsonDerivedType(typeof(ShearsBehaviorDefinition), "shears")]
[JsonDerivedType(typeof(FlintAndSteelBehaviorDefinition), "flint_and_steel")]
[JsonDerivedType(typeof(FishingRodBehaviorDefinition), "fishing_rod")]
[JsonDerivedType(typeof(BowBehaviorDefinition), "bow")]
[JsonDerivedType(typeof(BucketBehaviorDefinition), "bucket")]
[JsonDerivedType(typeof(MinecartBehaviorDefinition), "minecart")]
[JsonDerivedType(typeof(BoatBehaviorDefinition), "boat")]
[JsonDerivedType(typeof(BedBehaviorDefinition), "bed")]
[JsonDerivedType(typeof(DoorBehaviorDefinition), "door")]
[JsonDerivedType(typeof(SeedsBehaviorDefinition), "seeds")]
[JsonDerivedType(typeof(PlaceBlockBehaviorDefinition), "place_block")]
[JsonDerivedType(typeof(ThrowableBehaviorDefinition), "throwable")]
[JsonDerivedType(typeof(DyeBehaviorDefinition), "dye")]
[JsonDerivedType(typeof(CoalBehaviorDefinition), "coal")]
[JsonDerivedType(typeof(RecordBehaviorDefinition), "record")]
[JsonDerivedType(typeof(RedstoneBehaviorDefinition), "redstone")]
[JsonDerivedType(typeof(SignBehaviorDefinition), "sign")]
[JsonDerivedType(typeof(PaintingBehaviorDefinition), "painting")]
[JsonDerivedType(typeof(SaddleBehaviorDefinition), "saddle")]
[JsonDerivedType(typeof(MapBehaviorDefinition), "map")]
public abstract class ItemBehaviorDefinition
{
    public abstract IItemBehavior Build();
}

public sealed class FoodBehaviorDefinition : ItemBehaviorDefinition
{
    public int HealAmount { get; init; }
    public bool IsMeat { get; init; }
    public string? ReturnItem { get; init; }

    public override IItemBehavior Build()
    {
        Item? returnItem;
        if (ReturnItem != null)
        {
            if (DefaultRegistries.Items.Get(ResourceLocation.Parse(ReturnItem))?.Value is { } def)
                returnItem = Item.ITEMS[def.ProtocolId];
            else
                throw new ArgumentException($"Unknown item: '{ReturnItem}'", nameof(ReturnItem));
        }
        else
        {
            returnItem = null;
        }

        return new FoodBehavior(
            HealAmount,
            IsMeat,
            returnItem
        );
    }
}

public sealed class ToolBehaviorDefinition : ItemBehaviorDefinition
{
    public string ToolType { get; init; } = "shovel"; // "shovel", "pickaxe", "axe"
    public string Material { get; init; } = "iron";

    public override IItemBehavior Build()
    {
        var mat = ToolMaterialRegistry.Get(Material);
        return ToolType switch
        {
            "pickaxe" => new ToolBehavior(mat, 2, Item.s_pickaxeBlocks, Item.PickaxeSuitableFor(mat)),
            "axe" => new ToolBehavior(mat, 3, Item.s_axeBlocks),
            _ => new ToolBehavior(mat, 1, Item.s_spadeBlocks, b => b == Block.Snow || b == Block.SnowBlock),
        };
    }
}

public sealed class SwordBehaviorDefinition : ItemBehaviorDefinition
{
    public string Material { get; init; } = "iron";
    public override IItemBehavior Build() => new SwordBehavior(ToolMaterialRegistry.Get(Material));
}

public sealed class HoeBehaviorDefinition : ItemBehaviorDefinition
{
    public string Material { get; init; } = "iron";
    public override IItemBehavior Build() => new HoeBehavior(ToolMaterialRegistry.Get(Material));
}

public sealed class ArmorBehaviorDefinition : ItemBehaviorDefinition
{
    public string Material { get; init; } = "iron";
    public ArmorSlot Slot { get; init; }
    public override IItemBehavior Build() => new ArmorBehavior(ArmorMaterialRegistry.Get(Material), Slot);
}

public sealed class ShearsBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new ShearsBehavior();
}

public sealed class FlintAndSteelBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new FlintAndSteelBehavior();
}

public sealed class FishingRodBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new FishingRodBehavior();
}

public sealed class BowBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new BowBehavior();
}

public sealed class BucketBehaviorDefinition : ItemBehaviorDefinition
{
    public string Liquid { get; init; } = "empty"; // "empty", "water", "lava", "milk"

    public override IItemBehavior Build() => new BucketBehavior(Liquid switch
    {
        "water" => Block.FlowingWater.id,
        "lava" => Block.FlowingLava.id,
        "milk" => -1,
        _ => 0,
    });
}

public sealed class MinecartBehaviorDefinition : ItemBehaviorDefinition
{
    public int CartType { get; init; } // 0=normal, 1=chest, 2=furnace
    public override IItemBehavior Build() => new MinecartBehavior(CartType);
}

public sealed class BoatBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new BoatBehavior();
}

public sealed class BedBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new BedBehavior();
}

public sealed class DoorBehaviorDefinition : ItemBehaviorDefinition
{
    public string DoorMaterial { get; init; } = "wood"; // "wood" or "iron"

    public override IItemBehavior Build() => new DoorBehavior(
        DoorMaterial == "iron" ? Material.Metal : Material.Wood);
}

public sealed class SeedsBehaviorDefinition : ItemBehaviorDefinition
{
    public string? PlacesBlock { get; init; }
    public override IItemBehavior Build()
    {
        var block = Block.ByName(PlacesBlock!);
        return new SeedsBehavior(block!.id);
    }
}

public sealed class PlaceBlockBehaviorDefinition : ItemBehaviorDefinition
{
    public string? PlacesBlock { get; init; }
    public override IItemBehavior Build()
    {
        var block = Block.ByName(PlacesBlock!);
        return new PlaceBlockBehavior(block!);
    }
}

public sealed class ThrowableBehaviorDefinition : ItemBehaviorDefinition
{
    public string ProjectileType { get; init; } = "snowball"; // "snowball" or "egg"

    public override IItemBehavior Build()
    {
        Func<IWorldContext, EntityPlayer, Entity> factory = ProjectileType switch
        {
            "egg" => (w, p) => new EntityEgg(w, p),
            _ => (w, p) => new EntitySnowball(w, p),
        };
        return new ThrowableBehavior(factory);
    }
}

public sealed class DyeBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new DyeBehavior();
}

public sealed class CoalBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new CoalBehavior();
}

public sealed class RecordBehaviorDefinition : ItemBehaviorDefinition
{
    public string RecordName { get; init; } = string.Empty;
    public override IItemBehavior Build() => new RecordBehavior(RecordName);
}

public sealed class RedstoneBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new RedstoneBehavior();
}

public sealed class SignBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new SignBehavior();
}

public sealed class PaintingBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new PaintingBehavior();
}

public sealed class SaddleBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new SaddleBehavior();
}

public sealed class MapBehaviorDefinition : ItemBehaviorDefinition
{
    public override IItemBehavior Build() => new MapBehavior();
}
