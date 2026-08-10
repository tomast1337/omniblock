using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class DoorBehavior : IItemBehavior
{
    private readonly Material _doorMaterial;

    internal DoorBehavior(Material doorMaterial) => _doorMaterial = doorMaterial;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int side)
    {
        if (side != 1)
        {
            return false;
        }

        y++;

        int blockId = _doorMaterial == MaterialRegistry.Get("wood") ? BlockRegistry.Get("door").Id : BlockRegistry.Get("iron_door").Id;
        if (!Block.Blocks[blockId].CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            return false;
        }

        int facing = MathHelper.Floor((player.Yaw + 180.0f) * 4.0f / 360.0f - 0.5f) & 3;
        int offsetX = 0;
        int offsetZ = 0;
        if (facing == 0)
        {
            offsetZ = 1;
        }

        if (facing == 1)
        {
            offsetX = -1;
        }

        if (facing == 2)
        {
            offsetZ = -1;
        }

        if (facing == 3)
        {
            offsetX = 1;
        }

        int leftSolid = (world.Reader.ShouldSuffocate(x - offsetX, y, z - offsetZ) ? 1 : 0) +
                        (world.Reader.ShouldSuffocate(x - offsetX, y + 1, z - offsetZ) ? 1 : 0);
        int rightSolid = (world.Reader.ShouldSuffocate(x + offsetX, y, z + offsetZ) ? 1 : 0) +
                         (world.Reader.ShouldSuffocate(x + offsetX, y + 1, z + offsetZ) ? 1 : 0);
        bool leftHasDoor = world.Reader.GetBlockId(x - offsetX, y, z - offsetZ) == blockId ||
                           world.Reader.GetBlockId(x - offsetX, y + 1, z - offsetZ) == blockId;
        bool rightHasDoor = world.Reader.GetBlockId(x + offsetX, y, z + offsetZ) == blockId ||
                            world.Reader.GetBlockId(x + offsetX, y + 1, z + offsetZ) == blockId;
        bool mirror = (leftHasDoor && !rightHasDoor) || rightSolid > leftSolid;

        if (mirror)
        {
            facing = (facing - 1) & 3;
            facing += 4;
        }

        world.Writer.SetBlock(x, y, z, blockId, facing);
        world.Writer.SetBlock(x, y + 1, z, blockId, facing + 8);
        world.Broadcaster.NotifyNeighbors(x, y, z, blockId);
        world.Broadcaster.NotifyNeighbors(x, y + 1, z, blockId);
        itemStack.ConsumeItem(player);
        return true;
    }

    public IReadOnlyList<string> GetItemAliases(Item item)
        => _doorMaterial == MaterialRegistry.Get("wood") ? ["door", "woodDoor"] : [];
}
