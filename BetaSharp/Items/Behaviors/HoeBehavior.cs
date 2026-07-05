using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class HoeBehavior : IItemBehavior
{
    private readonly ToolMaterial _toolMaterial;

    internal HoeBehavior(ToolMaterial toolMaterial) => _toolMaterial = toolMaterial;

    public void Apply(Item item) => item.setMaxDamage(_toolMaterial.MaxUses);

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        int targetBlockId = world.Reader.GetBlockId(x, y, z);
        int blockAbove = world.Reader.GetBlockId(x, y + 1, z);
        if ((meta == 0 || blockAbove != 0 || targetBlockId != Block.GrassBlock.id) && targetBlockId != Block.Dirt.id)
        {
            return false;
        }

        Block block = Block.Farmland;
        world.Broadcaster.PlaySoundAtPos(x + 0.5F, y + 0.5F, z + 0.5F, block.SoundGroup.StepSound, (block.SoundGroup.Volume + 1.0F) / 2.0F, block.SoundGroup.Pitch * 0.8F);
        if (world.IsRemote)
        {
            return true;
        }

        world.Writer.SetBlock(x, y, z, block.id);
        itemStack.DamageItem(1, player);
        return true;
    }

    public bool IsHandheld(Item item) => true;
}
