using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Registries;
using OmniBlock.Recipes;

namespace OmniBlock.Stats;

public static class Stats
{
    public static Dictionary<int, StatBase> IdToStat = [];
    public static List<StatBase> AllStats = [];
    public static List<StatBase> GeneralStats = [];
    public static List<StatBase> ItemStats = [];
    public static List<StatBase> BlocksMinedStats = [];

    public static StatBase StartGameStat = new StatBasic(1000, "stat.startGame").SetLocalOnly().RegisterStat();
    public static StatBase CreateWorldStat = new StatBasic(1001, "stat.createWorld").SetLocalOnly().RegisterStat();
    public static StatBase LoadWorldStat = new StatBasic(1002, "stat.loadWorld").SetLocalOnly().RegisterStat();
    public static StatBase JoinMultiplayerStat = new StatBasic(1003, "stat.joinMultiplayer").SetLocalOnly().RegisterStat();
    public static StatBase LeaveGameStat = new StatBasic(1004, "stat.leaveGame").SetLocalOnly().RegisterStat();
    public static StatBase MinutesPlayedStat = new StatBasic(1100, "stat.playOneMinute", StatFormatters.FormatTime).SetLocalOnly().RegisterStat();
    public static StatBase DistanceWalkedStat = new StatBasic(2000, "stat.walkOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceSwumStat = new StatBasic(2001, "stat.swimOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceFallenStat = new StatBasic(2002, "stat.fallOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceClimbedStat = new StatBasic(2003, "stat.climbOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceFlownStat = new StatBasic(2004, "stat.flyOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceDoveStat = new StatBasic(2005, "stat.diveOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceByMinecartStat = new StatBasic(2006, "stat.minecartOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceByBoatStat = new StatBasic(2007, "stat.boatOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase DistanceByPigStat = new StatBasic(2008, "stat.pigOneCm", StatFormatters.FormatDistance).SetLocalOnly().RegisterStat();
    public static StatBase JumpStat = new StatBasic(2010, "stat.jump").SetLocalOnly().RegisterStat();
    public static StatBase DropStat = new StatBasic(2011, "stat.drop").SetLocalOnly().RegisterStat();
    public static StatBase DamageDealtStat = new StatBasic(2020, "stat.damageDealt").RegisterStat();
    public static StatBase DamageTakenStat = new StatBasic(2021, "stat.damageTaken").RegisterStat();
    public static StatBase DeathsStat = new StatBasic(2022, "stat.deaths").RegisterStat();
    public static StatBase MobKillsStat = new StatBasic(2023, "stat.mobKills").RegisterStat();
    public static StatBase PlayerKillsStat = new StatBasic(2024, "stat.playerKills").RegisterStat();
    public static StatBase FishCaughtStat = new StatBasic(2025, "stat.fishCaught").RegisterStat();

    public static StatBase[] MineBlockStatArray = InitBlocksMined("stat.mineBlock", 16777216);
    public static StatBase[] Crafted;
    public static StatBase[] Used;
    public static StatBase[] Broken;

    private static bool _hasBasicItemStatsInitialized;
    private static bool _hasExtendedItemStatsInitialized;

    public static void InitializeItemStats(ContentRuntimeBuilder content)
    {
        OmniBlock.Achievements.Initialize(content);
        Used = InitItemUsedStats(content, Used, "stat.useItem", 16908288, 0, BlockRegistry.ProtocolIdCapacity);
        Broken = InitializeBrokenItemStats(content, Broken, "stat.breakItem", 16973824, 0, BlockRegistry.ProtocolIdCapacity);
        _hasBasicItemStatsInitialized = true;
        InitializeCraftedItemStats(content);
    }

    public static void InitializeExtendedItemStats(IItemRuntimeView items)
    {
        Used = InitItemUsedStats(items, Used, "stat.useItem", 16908288, BlockRegistry.ProtocolIdCapacity, 32000);
        Broken = InitializeBrokenItemStats(items, Broken, "stat.breakItem", 16973824, BlockRegistry.ProtocolIdCapacity, 32000);
        _hasExtendedItemStatsInitialized = true;
        InitializeCraftedItemStats(items);
    }

    public static void InitializeCraftedItemStats(IItemRuntimeView items)
    {
        if (_hasBasicItemStatsInitialized && _hasExtendedItemStatsInitialized)
        {
            HashSet<int> craftedIds = new HashSet<int>();

            foreach (IRecipe recipe in RecipesCrafting.Recipes.Values)
            {
                craftedIds.Add(recipe.GetRecipeOutput().ItemId);
            }

            foreach (ItemStack itemStack in RecipesSmelting.Recipes.Values)
            {
                craftedIds.Add(itemStack.ItemId);
            }

            Crafted = new StatBase[32000];

            foreach (int itemId in craftedIds)
            {
                if (items.TryGetByProtocolId(itemId, out Item? item) && item is not null)
                {
                    string translatedName = StatCollector.TranslateToLocalFormatted("stat.craftItem", item.GetStatName());
                    Crafted[itemId] = new StatCrafting(16842752 + itemId, translatedName, itemId).RegisterStat();
                }
            }

            ReplaceAllSimilarBlocks(Crafted);
        }
    }

    private static StatBase[] InitBlocksMined(string baseName, int baseId)
    {
        StatBase[] statsArray = new StatBase[256];

        for (int i = 0; i < 256; ++i)
        {
            if (BlockRegistry.TryGetByProtocolId(i, out Block? block) && block.EnableStats)
            {
                string translatedName = StatCollector.TranslateToLocalFormatted(baseName, block.TranslateBlockName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();
                BlocksMinedStats.Add(statsArray[i]);
            }
        }

        ReplaceAllSimilarBlocks(statsArray);
        return statsArray;
    }

    private static StatBase[] InitItemUsedStats(IItemRuntimeView items, StatBase[] statsArray, string baseName, int baseId, int startIdx, int endIdx)
    {
        statsArray ??= new StatBase[32000];

        for (int i = startIdx; i < endIdx; ++i)
        {
            if (items.TryGetByProtocolId(i, out Item? item) && item is not null)
            {
                string translatedName = StatCollector.TranslateToLocalFormatted(baseName, item.GetStatName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();

                if (i >= BlockRegistry.ProtocolIdCapacity)
                {
                    ItemStats.Add(statsArray[i]);
                }
            }
        }

        ReplaceAllSimilarBlocks(statsArray);
        return statsArray;
    }

    private static StatBase[] InitializeBrokenItemStats(IItemRuntimeView items, StatBase[] statsArray, string baseName, int baseId, int startIdx, int endIdx)
    {
        statsArray ??= new StatBase[32000];

        for (int i = startIdx; i < endIdx; ++i)
        {
            if (items.TryGetByProtocolId(i, out Item? item) && item is not null && item.IsDamagable())
            {
                string translatedName = StatCollector.TranslateToLocalFormatted(baseName, item.GetStatName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();
            }
        }

        ReplaceAllSimilarBlocks(statsArray);
        return statsArray;
    }

    private static void ReplaceAllSimilarBlocks(StatBase[] statsArray)
    {
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("water").Id, BlockRegistry.Get("flowing_water").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("lava").Id, BlockRegistry.Get("lava").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("jack_lantern").Id, BlockRegistry.Get("pumpkin").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("lit_furnace").Id, BlockRegistry.Get("furnace").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("lit_redstone_ore").Id, BlockRegistry.Get("redstone_ore").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("powered_repeater").Id, BlockRegistry.Get("repeater").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("lit_redstone_torch").Id, BlockRegistry.Get("redstone_torch").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("red_mushroom").Id, BlockRegistry.Get("brown_mushroom").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("double_slab").Id, BlockRegistry.Get("slab").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("grass_block").Id, BlockRegistry.Get("dirt").Id);
        ReplaceSimilarBlocks(statsArray, BlockRegistry.Get("farmland").Id, BlockRegistry.Get("dirt").Id);
    }

    private static void ReplaceSimilarBlocks(StatBase[] statsArray, int sourceId, int targetId)
    {
        if (statsArray[sourceId] != null && statsArray[targetId] == null)
        {
            statsArray[targetId] = statsArray[sourceId];
        }
        else
        {
            AllStats.Remove(statsArray[sourceId]);
            BlocksMinedStats.Remove(statsArray[sourceId]);
            GeneralStats.Remove(statsArray[sourceId]);
            statsArray[sourceId] = statsArray[targetId];
        }
    }

    public static StatBase GetStatById(int id)
    {
        return IdToStat[id];
    }

}
