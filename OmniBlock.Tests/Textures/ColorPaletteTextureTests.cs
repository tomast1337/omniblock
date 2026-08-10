using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Textures;

/// <summary>
///     Pins wool and dye to the exact tiles the grid arithmetic they replaced produced. Both walked
///     a base index across two columns of eight, so a mistake in the replacement list shows up as a
///     wrong-coloured icon and nothing else — no exception, no failing draw.
/// </summary>
public sealed class ColorPaletteTextureTests
{
    private static readonly string[] s_woolByBlockMeta =
    [
        "white_wool", "orange_wool", "magenta_wool", "light_blue_wool",
        "yellow_wool", "lime_wool", "pink_wool", "gray_wool",
        "light_gray_wool", "cyan_wool", "purple_wool", "blue_wool",
        "brown_wool", "green_wool", "red_wool", "black_wool"
    ];

    private static readonly string[] s_dyeByDamage =
    [
        "ink_sac", "rose_red", "cactus_green", "cocoa_beans",
        "lapis_lazuli", "purple_dye", "cyan_dye", "light_gray_dye",
        "gray_dye", "pink_dye", "lime_dye", "dandelion_yellow",
        "light_blue_dye", "magenta_dye", "orange_dye", "bone_meal"
    ];

    [Fact]
    public void Wool_texture_per_block_meta_matches_the_legacy_palette_walk()
    {
        Block wool = BlockRegistry.Get("wool");

        for (int meta = 0; meta < 16; meta++)
        {
            Assert.Equal(Atlases.Terrain.IndexOf(s_woolByBlockMeta[meta]), wool.GetTexture(Side.North, meta));
        }
    }

    [Fact]
    public void Dye_texture_per_damage_matches_the_legacy_palette_walk()
    {
        Item dye = Item.ByName("dye_powder");

        for (int damage = 0; damage < 16; damage++)
        {
            Assert.Equal(Atlases.Items.IndexOf(s_dyeByDamage[damage]), dye.GetTextureId(damage));
        }
    }

    /// <summary>
    ///     The dye tiles the arithmetic reached and the names the atlas gives them agree. They did
    ///     not before: the two columns had been transposed in the atlas, harmless only because
    ///     nothing resolved a dye by name.
    /// </summary>
    [Fact]
    public void Dye_atlas_names_agree_with_the_translation_key_order()
    {
        string[] expected =
        [
            "black", "red", "green", "brown", "blue", "purple", "cyan", "silver",
            "gray", "pink", "lime", "yellow", "lightBlue", "magenta", "orange", "white"
        ];

        Item dye = Item.ByName("dye_powder");
        int inkSac = Atlases.Items.IndexOf("ink_sac");

        for (int damage = 0; damage < 16; damage++)
        {
            Assert.Equal(inkSac + damage % 8 * 16 + damage / 8, dye.GetTextureId(damage));
            Assert.EndsWith("." + expected[damage], dye.GetItemNameIs(new ItemStack(dye, 1, damage)), StringComparison.Ordinal);
        }
    }
}
