using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

namespace BetaSharp.Blocks;

internal class BlockOre(int id, int textureId) : Block(id, textureId, Material.Stone)
{
    public override int getDroppedItemId(int blockMeta) => id == CoalOre.id ? Item.ByName("coal").id : id == DiamondOre.id ? Item.ByName("emerald").id : id == LapisOre.id ? Item.ByName("dye_powder").id : id;

    public override int getDroppedItemCount() => id == LapisOre.id ? 4 + Random.Shared.Next(5) : 1;

    protected override int getDroppedItemMeta(int blockMeta) => id == LapisOre.id ? 4 : 0;
}
