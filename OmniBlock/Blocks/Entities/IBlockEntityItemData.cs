using OmniBlock.Items;

namespace OmniBlock.Blocks.Entities;

/// <summary>
///     Applies persistent, item-owned configuration after a block item creates its block entity.
///     The placement behavior knows only this contract; individual block entities own their schema.
/// </summary>
public interface IBlockEntityItemData
{
    void ApplyItemData(ItemStack stack);
}
