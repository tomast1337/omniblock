using OmniBlock.Processes;
using OmniBlock.Registries;

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

    public static StatBase[] MineBlockStatArray = new StatBase[RuntimeBlockRegistry.ProtocolIdCapacity];
    public static StatBase[] Crafted;
    public static StatBase[] Used;
    public static StatBase[] Broken;

    private static bool _hasBasicItemStatsInitialized;
    private static bool _hasExtendedItemStatsInitialized;

    public static void InitializeItemStats(ContentRuntimeBuilder content)
    {
        OmniBlock.Achievements.Initialize(content);
        MineBlockStatArray = InitBlocksMined(content, "stat.mineBlock", 16777216);
        Used = InitItemUsedStats(content, Used, "stat.useItem", 16908288, 0, RuntimeBlockRegistry.ProtocolIdCapacity);
        Broken = InitializeBrokenItemStats(content, Broken, "stat.breakItem", 16973824, 0, RuntimeBlockRegistry.ProtocolIdCapacity);
        _hasBasicItemStatsInitialized = true;
    }

    public static void InitializeExtendedItemStats(IItemRuntimeView items)
    {
        Used = InitItemUsedStats(items, Used, "stat.useItem", 16908288, RuntimeBlockRegistry.ProtocolIdCapacity, 32000);
        Broken = InitializeBrokenItemStats(items, Broken, "stat.breakItem", 16973824, RuntimeBlockRegistry.ProtocolIdCapacity, 32000);
        _hasExtendedItemStatsInitialized = true;
    }

    public static void InitializeCraftedItemStats(
        IItemRuntimeView items,
        RuntimeProcessRegistry processes)
    {
        if (_hasBasicItemStatsInitialized && _hasExtendedItemStatsInitialized)
        {
            var craftedIds = new HashSet<int>();

            foreach (var process in processes.Values)
            {
                switch (process)
                {
                    case ICompiledCraftingProcess crafting:
                        craftedIds.Add(crafting.Output.Item.Id);
                        break;
                    case ICompiledSmeltingProcess smelting:
                        craftedIds.Add(smelting.Output.Item.Id);
                        break;
                }
            }

            Crafted = new StatBase[32000];

            foreach (var itemId in craftedIds)
            {
                if (items.TryGetByProtocolId(itemId, out var item) && item is not null)
                {
                    var translatedName = StatCollector.TranslateToLocalFormatted("stat.craftItem", item.GetStatName());
                    Crafted[itemId] = new StatCrafting(16842752 + itemId, translatedName, itemId).RegisterStat();
                }
            }

            ReplaceAllSimilarBlocks(Crafted, items);
        }
    }

    private static StatBase[] InitBlocksMined(ContentRuntimeBuilder content, string baseName, int baseId)
    {
        var statsArray = new StatBase[256];

        for (var i = 0; i < 256; ++i)
        {
            if (content.TryGetBlockByProtocolId(i, out var block) && block is not null && block.EnableStats)
            {
                var translatedName = StatCollector.TranslateToLocalFormatted(baseName, block.TranslateBlockName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();
                BlocksMinedStats.Add(statsArray[i]);
            }
        }

        ReplaceAllSimilarBlocks(statsArray, content);
        return statsArray;
    }

    private static StatBase[] InitItemUsedStats(IItemRuntimeView items, StatBase[] statsArray, string baseName, int baseId, int startIdx, int endIdx)
    {
        statsArray ??= new StatBase[32000];

        for (var i = startIdx; i < endIdx; ++i)
        {
            if (items.TryGetByProtocolId(i, out var item) && item is not null)
            {
                var translatedName = StatCollector.TranslateToLocalFormatted(baseName, item.GetStatName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();

                if (i >= RuntimeBlockRegistry.ProtocolIdCapacity)
                {
                    ItemStats.Add(statsArray[i]);
                }
            }
        }

        ReplaceAllSimilarBlocks(statsArray, items);
        return statsArray;
    }

    private static StatBase[] InitializeBrokenItemStats(IItemRuntimeView items, StatBase[] statsArray, string baseName, int baseId, int startIdx, int endIdx)
    {
        statsArray ??= new StatBase[32000];

        for (var i = startIdx; i < endIdx; ++i)
        {
            if (items.TryGetByProtocolId(i, out var item) && item is not null && item.IsDamagable())
            {
                var translatedName = StatCollector.TranslateToLocalFormatted(baseName, item.GetStatName());
                statsArray[i] = new StatCrafting(baseId + i, translatedName, i).RegisterStat();
            }
        }

        ReplaceAllSimilarBlocks(statsArray, items);
        return statsArray;
    }

    private static void ReplaceAllSimilarBlocks(StatBase[] statsArray, IItemRuntimeView items)
    {
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:water")).Id, items.Get(ResourceLocation.Parse("omniblock:flowing_water")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:lava")).Id, items.Get(ResourceLocation.Parse("omniblock:lava")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:jack_lantern")).Id, items.Get(ResourceLocation.Parse("omniblock:pumpkin")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:lit_furnace")).Id, items.Get(ResourceLocation.Parse("omniblock:furnace")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:lit_redstone_ore")).Id, items.Get(ResourceLocation.Parse("omniblock:redstone_ore")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:powered_repeater")).Id, items.Get(ResourceLocation.Parse("omniblock:repeater")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:lit_redstone_torch")).Id, items.Get(ResourceLocation.Parse("omniblock:redstone_torch")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:red_mushroom")).Id, items.Get(ResourceLocation.Parse("omniblock:brown_mushroom")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:double_slab")).Id, items.Get(ResourceLocation.Parse("omniblock:slab")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:grass_block")).Id, items.Get(ResourceLocation.Parse("omniblock:dirt")).Id);
        ReplaceSimilarBlocks(statsArray, items.Get(ResourceLocation.Parse("omniblock:farmland")).Id, items.Get(ResourceLocation.Parse("omniblock:dirt")).Id);
    }

    private static void ReplaceAllSimilarBlocks(StatBase[] statsArray, ContentRuntimeBuilder content)
    {
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:water")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:flowing_water")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:lava")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:lava")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:jack_lantern")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:pumpkin")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:lit_furnace")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:furnace")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:lit_redstone_ore")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:redstone_ore")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:powered_repeater")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:repeater")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:lit_redstone_torch")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:redstone_torch")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:red_mushroom")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:brown_mushroom")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:double_slab")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:slab")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:grass_block")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:dirt")).Id);
        ReplaceSimilarBlocks(statsArray, content.GetBlock(ResourceLocation.Parse("omniblock:farmland")).Id, content.GetBlock(ResourceLocation.Parse("omniblock:dirt")).Id);
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

    public static StatBase GetStatById(int id) => IdToStat[id];
}
