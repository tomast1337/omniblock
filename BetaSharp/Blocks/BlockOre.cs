using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

namespace BetaSharp.Blocks;

internal class BlockOre(int id, int textureId) : Block(id, textureId, Material.Stone)
{
    private static readonly int s_coalId = Item.ByName("coal").Id;
    private static readonly int s_diamondId = Item.ByName("diamond").Id;
    private static readonly int s_dyePowderId = Item.ByName("dye_powder").Id;

    public override int getDroppedItemId(int blockMeta) => id == CoalOre.id ? s_coalId : id == DiamondOre.id ? s_diamondId : id == LapisOre.id ? s_dyePowderId : id;

    public override int getDroppedItemCount() => id == LapisOre.id ? 4 + Random.Shared.Next(5) : 1;

    protected override int getDroppedItemMeta(int blockMeta) => id == LapisOre.id ? 4 : 0;
}
