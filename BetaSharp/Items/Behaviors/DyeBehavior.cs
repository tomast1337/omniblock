using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class DyeBehavior(int[] textures) : IItemBehavior
{
    internal static readonly string[] ColorNames =
    [
        "black", "red", "green", "brown", "blue", "purple", "cyan", "silver",
        "gray", "pink", "lime", "yellow", "lightBlue", "magenta", "orange", "white"
    ];

    // A plain list in damage order, not a base index walked as `base + meta % 8 * 16 + meta / 8`.
    // That arithmetic hard-coded the dyes sitting in two columns of eight on gui/items.png, which
    // no definition outside this assembly could reproduce and a seventeenth dye could not extend.
    public int GetTextureId(Item item, int meta) => textures[meta & 15];

    public string GetItemNameIS(Item item, ItemStack itemStack)
        => item.GetItemName() + "." + ColorNames[itemStack.GetDamage()];

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (itemStack.GetDamage() != 15)
        {
            return false;
        }

        int blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == BlockRegistry.Get("sapling").Id)
        {
            if (!world.IsRemote)
            {
                SaplingBehavior.Generate(world, x, y, z);
                itemStack.ConsumeItem(player);
            }

            return true;
        }

        if (blockId == BlockRegistry.Get("wheat").Id)
        {
            if (!world.IsRemote)
            {
                CropBehavior.ApplyFullGrowth(world, x, y, z);
                itemStack.ConsumeItem(player);
            }

            return true;
        }

        if (blockId == BlockRegistry.Get("grass_block").Id)
        {
            if (!world.IsRemote)
            {
                itemStack.ConsumeItem(player);
                for (int attempt = 0; attempt < 128; ++attempt)
                {
                    int spawnX = x;
                    int spawnY = y + 1;
                    int spawnZ = z;
                    bool validPosition = true;
                    for (int walkStep = 0; walkStep < attempt / 16 && validPosition; ++walkStep)
                    {
                        spawnX += Item.s_itemRand.NextInt(3) - 1;
                        spawnY += (Item.s_itemRand.NextInt(3) - 1) * Item.s_itemRand.NextInt(3) / 2;
                        spawnZ += Item.s_itemRand.NextInt(3) - 1;
                        if (world.Reader.GetBlockId(spawnX, spawnY - 1, spawnZ) != BlockRegistry.Get("grass_block").Id || world.Reader.ShouldSuffocate(spawnX, spawnY, spawnZ))
                        {
                            validPosition = false;
                        }
                    }

                    if (validPosition && world.Reader.GetBlockId(spawnX, spawnY, spawnZ) == 0)
                    {
                        if (Item.s_itemRand.NextInt(10) != 0)
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, BlockRegistry.Get("grass").Id, 1);
                        }
                        else if (Item.s_itemRand.NextInt(3) != 0)
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, BlockRegistry.Get("dandelion").Id);
                        }
                        else
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, BlockRegistry.Get("rose").Id);
                        }
                    }
                }
            }

            return true;
        }

        return false;
    }

    public void UseOnEntity(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        // Dyeable because it has a fleece, not because it is a sheep.
        if (target.Behaviors.Find<WoolBehavior>() is { } wool)
        {
            int woolColor = ClothVisualBehavior.GetBlockMeta(itemStack.GetDamage());
            if (!wool.IsShearedOn(target) && wool.ColorOf(target) != woolColor)
            {
                wool.SetColorOn(target, woolColor);
                itemStack.ConsumeItem(player);
            }
        }
    }

    public IReadOnlyList<string> GetItemAliases(Item item) =>
    [
        "blackDye:0", "redDye:1", "greenDye:2", "brownDye:3", "blueDye:4",
        "purpleDye:5", "cyanDye:6", "silverDye:7", "grayDye:8", "pinkDye:9",
        "limeDye:10", "yellowDye:11", "lightBlueDye:12", "magentaDye:13",
        "orangeDye:14", "whiteDye:15",
        "inkSac:0", "roseRed:1", "cactusGreen:2", "lapisLazuli:4", "lapis:4",
        "lightGrayDye:7", "dendelionYellow:11", "boneMeal:15"
    ];
}
