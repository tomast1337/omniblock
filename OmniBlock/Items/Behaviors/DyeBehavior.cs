using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class DyeBehavior(int[] textures) : IItemBehavior
{
    internal static readonly string[] s_colorNames =
    [
        "black", "red", "green", "brown", "blue", "purple", "cyan", "silver",
        "gray", "pink", "lime", "yellow", "lightBlue", "magenta", "orange", "white"
    ];

    // A plain list in damage order, not a base index walked as `base + meta % 8 * 16 + meta / 8`.
    // That arithmetic hard-coded the dyes sitting in two columns of eight on gui/items.png, which
    // no definition outside this assembly could reproduce and a seventeenth dye could not extend.
    public int GetTextureId(Item item, int meta) => textures[meta & 15];

    public string GetItemNameIs(Item item, ItemStack itemStack) => $"{item.GetItemName()}.{s_colorNames[itemStack.GetDamage()]}";

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (itemStack.GetDamage() != 15) return false;

        var blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == world.Content.Blocks.Get("omniblock:sapling").Id)
        {
            if (world.IsRemote) return true;

            SaplingBehavior.Generate(world, x, y, z, blockId);
            itemStack.ConsumeItem(player);

            return true;
        }

        if (blockId == world.Content.Blocks.Get("omniblock:wheat").Id)
        {
            if (!world.IsRemote)
            {
                CropBehavior.ApplyFullGrowth(world, x, y, z);
                itemStack.ConsumeItem(player);
            }

            return true;
        }

        if (blockId != world.Content.Blocks.Get("omniblock:grass_block").Id) return false;

        if (world.IsRemote) return true;

        itemStack.ConsumeItem(player);

        for (var attempt = 0; attempt < 128; ++attempt)
        {
            var spawnX = x;
            var spawnY = y + 1;
            var spawnZ = z;
            var validPosition = true;
            for (var walkStep = 0; walkStep < attempt / 16 && validPosition; ++walkStep)
            {
                spawnX += Item.s_itemRand.NextInt(3) - 1;
                spawnY += (Item.s_itemRand.NextInt(3) - 1) * Item.s_itemRand.NextInt(3) / 2;
                spawnZ += Item.s_itemRand.NextInt(3) - 1;
                if (world.Reader.GetBlockId(spawnX, spawnY - 1, spawnZ) != world.Content.Blocks.Get("omniblock:grass_block").Id || world.Reader.ShouldSuffocate(spawnX, spawnY, spawnZ))
                {
                    validPosition = false;
                }
            }

            if (!validPosition || world.Reader.GetBlockId(spawnX, spawnY, spawnZ) != 0) continue;

            if (Item.s_itemRand.NextInt(10) != 0)
            {
                world.Writer.SetBlock(spawnX, spawnY, spawnZ, world.Content.Blocks.Get("omniblock:grass").Id, 1);
            }
            else if (Item.s_itemRand.NextInt(3) != 0)
            {
                world.Writer.SetBlock(spawnX, spawnY, spawnZ, world.Content.Blocks.Get("omniblock:dandelion").Id);
            }
            else
            {
                world.Writer.SetBlock(spawnX, spawnY, spawnZ, world.Content.Blocks.Get("omniblock:rose").Id);
            }
        }

        return true;
    }

    public void UseOnEntity(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        // Dyeable because it has a fleece, not because it is a sheep.

        if (target.Behaviors.Find<WoolBehavior>() is not { } wool) return;

        var woolColor = ClothVisualBehavior.GetBlockMeta(itemStack.GetDamage());

        if (wool.IsShearedOn(target) || wool.ColorOf(target) == woolColor) return;

        wool.SetColorOn(target, woolColor);
        itemStack.ConsumeItem(player);
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
