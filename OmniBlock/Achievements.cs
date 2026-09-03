using OmniBlock.Blocks;
using OmniBlock.Items;
using Microsoft.Extensions.Logging;

namespace OmniBlock;

public class Achievements
{
    public static int minColumn;
    public static int minRow;
    public static int maxColumn;
    public static int maxRow;

    public readonly static List<Achievement> AllAchievements = [];
    public static Achievement OpenInventory { get; private set; } = null!;
    public static Achievement MineWood { get; private set; } = null!;
    public static Achievement BuildWorkbench { get; private set; } = null!;
    public static Achievement BuildPickaxe { get; private set; } = null!;
    public static Achievement BuildFurnace { get; private set; } = null!;
    public static Achievement AcquireIron { get; private set; } = null!;
    public static Achievement BuildHoe { get; private set; } = null!;
    public static Achievement MakeBread { get; private set; } = null!;
    public static Achievement MakeCake { get; private set; } = null!;
    public static Achievement CraftStonePickaxe { get; private set; } = null!;
    public static Achievement CookFish { get; private set; } = null!;
    public static Achievement CraftRail { get; private set; } = null!;
    public static Achievement CraftSword { get; private set; } = null!;
    public static Achievement KillEnemy { get; private set; } = null!;
    public static Achievement KillCow { get; private set; } = null!;
    public static Achievement KillPig { get; private set; } = null!;

    public static void Initialize(Registries.ContentRuntimeBuilder content)
    {
        Item Item(string name) => content.Get(new ResourceLocation(Namespace.OmniBlock, name));
        Item BlockItem(string name) => content.GetByProtocolId(content.GetBlock(new ResourceLocation(Namespace.OmniBlock, name)).Id);
        OpenInventory = new Achievement(0, "openInventory", 0, 0, Item("book"), null!).m_66876377().registerAchievement();
        MineWood = new Achievement(1, "mineWood", 2, 1, BlockItem("log"), OpenInventory).registerAchievement();
        BuildWorkbench = new Achievement(2, "buildWorkBench", 4, -1, BlockItem("crafting_table"), MineWood).registerAchievement();
        BuildPickaxe = new Achievement(3, "buildPickaxe", 4, 2, Item("pickaxe_wood"), BuildWorkbench).registerAchievement();
        BuildFurnace = new Achievement(4, "buildFurnace", 3, 4, BlockItem("lit_furnace"), BuildPickaxe).registerAchievement();
        AcquireIron = new Achievement(5, "acquireIron", 1, 4, Item("ingot_iron"), BuildFurnace).registerAchievement();
        BuildHoe = new Achievement(6, "buildHoe", 2, -3, Item("hoe_wood"), BuildWorkbench).registerAchievement();
        MakeBread = new Achievement(7, "makeBread", -1, -3, Item("bread"), BuildHoe).registerAchievement();
        MakeCake = new Achievement(8, "bakeCake", 0, -5, Item("cake"), BuildHoe).registerAchievement();
        CraftStonePickaxe = new Achievement(9, "buildBetterPickaxe", 6, 2, Item("pickaxe_stone"), BuildPickaxe).registerAchievement();
        CookFish = new Achievement(10, "cookFish", 2, 6, Item("fish_cooked"), BuildFurnace).registerAchievement();
        CraftRail = new Achievement(11, "onARail", 2, 3, BlockItem("rail"), AcquireIron).challenge().registerAchievement();
        CraftSword = new Achievement(12, "buildSword", 6, -1, Item("sword_wood"), BuildWorkbench).registerAchievement();
        KillEnemy = new Achievement(13, "killEnemy", 8, -1, Item("bone"), CraftSword).registerAchievement();
        KillCow = new Achievement(14, "killCow", 7, -3, Item("leather"), CraftSword).registerAchievement();
        KillPig = new Achievement(15, "flyPig", 8, -4, Item("saddle"), KillCow).challenge().registerAchievement();
        Log.Instance.For<Achievements>().LogInformation("{Count} achievements", AllAchievements.Count);
    }

}
