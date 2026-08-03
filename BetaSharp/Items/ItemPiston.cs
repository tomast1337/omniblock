namespace BetaSharp.Items;

internal class ItemPiston : ItemBlock
{
    public ItemPiston(int id) : base(id)
    {
    }

    protected override int GetPlacementMetadata(int meta) => 7;
}
