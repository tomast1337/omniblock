using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class DyeBehavior : IItemBehavior
{
    internal static readonly string[] ColorNames =
    [
        "black", "red", "green", "brown", "blue", "purple", "cyan", "silver",
        "gray", "pink", "lime", "yellow", "lightBlue", "magenta", "orange", "white"
    ];

    internal static readonly int[] ColorValues =
    [
        0x1E1B1B, 0xB3312C, 0x3B511A, 0x51301A, 0x253192, 0x7B2FBE, 0x287697, 0x287697,
        0x434343, 0xD88198, 0x41CD34, 0xDECF2A, 0x6689D3, 0xC354CD, 0xEB8844, 0xF0F0F0
    ];

    public int GetTextureId(Item item, int meta)
        => item._textureId + meta % 8 * 16 + meta / 8;

    public string GetItemNameIS(Item item, ItemStack itemStack)
        => item.getItemName() + "." + ColorNames[itemStack.getDamage()];

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (itemStack.getDamage() != 15)
        {
            return false;
        }

        int blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == Block.Sapling.id)
        {
            if (!world.IsRemote)
            {
                ((BlockSapling)Block.Sapling).generate(world, x, y, z);
                itemStack.ConsumeItem(player);
            }

            return true;
        }

        if (blockId == Block.Wheat.id)
        {
            if (!world.IsRemote)
            {
                BlockCrops.applyFullGrowth(world, x, y, z);
                itemStack.ConsumeItem(player);
            }

            return true;
        }

        if (blockId == Block.GrassBlock.id)
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
                        spawnX += Item.itemRand.NextInt(3) - 1;
                        spawnY += (Item.itemRand.NextInt(3) - 1) * Item.itemRand.NextInt(3) / 2;
                        spawnZ += Item.itemRand.NextInt(3) - 1;
                        if (world.Reader.GetBlockId(spawnX, spawnY - 1, spawnZ) != Block.GrassBlock.id || world.Reader.ShouldSuffocate(spawnX, spawnY, spawnZ))
                        {
                            validPosition = false;
                        }
                    }

                    if (validPosition && world.Reader.GetBlockId(spawnX, spawnY, spawnZ) == 0)
                    {
                        if (Item.itemRand.NextInt(10) != 0)
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, Block.Grass.id, 1);
                        }
                        else if (Item.itemRand.NextInt(3) != 0)
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, Block.Dandelion.id);
                        }
                        else
                        {
                            world.Writer.SetBlock(spawnX, spawnY, spawnZ, Block.Rose.id);
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
        if (target is EntitySheep sheep)
        {
            int woolColor = BlockCloth.getBlockMeta(itemStack.getDamage());
            if (!sheep.IsSheared && sheep.FleeceColor != woolColor)
            {
                sheep.FleeceColor = woolColor;
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
