using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Rules;
using BetaSharp.Stats;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks;

public class Block
{
    public static BlockSoundGroup SoundPowderFootstep => SoundGroupRegistry.Get("powder");
    public static BlockSoundGroup SoundWoodFootstep => SoundGroupRegistry.Get("wood");
    public static BlockSoundGroup SoundGravelFootstep => SoundGroupRegistry.Get("gravel");
    public static BlockSoundGroup SoundGrassFootstep => SoundGroupRegistry.Get("grass");
    public static BlockSoundGroup SoundStoneFootstep => SoundGroupRegistry.Get("stone");
    public static BlockSoundGroup SoundMetalFootstep => SoundGroupRegistry.Get("metal");
    public static BlockSoundGroup SoundGlassFootstep => SoundGroupRegistry.Get("glass");
    public static BlockSoundGroup SoundClothFootstep => SoundGroupRegistry.Get("cloth");
    public static BlockSoundGroup SoundSandFootstep => SoundGroupRegistry.Get("sand");

    public static readonly Block[] Blocks = new Block[256];
    public static readonly bool[] BlocksRandomTick = new bool[256];
    public static readonly bool[] BlocksOpaque = new bool[256];
    public static readonly bool[] BlocksWithEntity = new bool[256];
    public static readonly int[] BlockLightOpacity = new int[256];
    public static readonly bool[] BlocksAllowVision = new bool[256];
    public static readonly int[] BlocksLightLuminance = new int[256];
    public static readonly bool[] BlocksIgnoreMetaUpdate = new bool[256];

    // Stateless and shared: declared before the block fields below because static
    // field initializers run in textual order.
    private static readonly FallingBlockBehavior s_fallingBehavior = new();
    private static readonly PlantSurvivalBehavior s_plantSurvival = new();
    private static readonly PlantSurvivalBehavior s_deadBushSurvival = new(id => id == Sand.id);
    private static readonly LogBehavior s_logBehavior = new();
    private static readonly MeltBehavior s_iceMelt = new(() => Water.id, subtractOpacity: true, () => FlowingWater.id);
    private static readonly MeltBehavior s_snowMelt = new(() => 0);
    private static readonly RedstoneWireBehavior s_redstoneWire = new();
    private static readonly ButtonBehavior s_buttonBehavior = new();
    private static readonly LeverBehavior s_leverBehavior = new();
    private static readonly PressurePlateBehavior s_mobPlate = new(PressurePlateActiviationRule.MOBS);
    private static readonly PressurePlateBehavior s_everythingPlate = new(PressurePlateActiviationRule.EVERYTHING);
    private static readonly DetectorRailBehavior s_detectorRail = new();
    private static readonly NoteBlockBehavior s_noteblockBehavior = new();
    private static readonly RedstoneOreBehavior s_redstoneOreBehavior = new();
    private static readonly RepeaterBehavior s_repeaterBehavior = new();
    private static readonly TileEntityLifecycleBehavior s_tileEntityLifecycle = new();
    private static readonly FurnaceBehavior s_furnaceLit = new(true);
    private static readonly FurnaceBehavior s_furnaceUnlit = new(false);
    private static readonly DispenserBehavior s_dispenserBehavior = new();
    private static readonly ChestBehavior s_chestBehavior = new();
    private static readonly JukeboxBehavior s_jukeboxBehavior = new();
    private static readonly SignBehavior s_standingSign = new(true);
    private static readonly SignBehavior s_wallSign = new(false);
    private static readonly SlabBehavior s_singleSlab = new(false);
    private static readonly SlabBehavior s_doubleSlab = new(true);
    private static readonly StairsBehavior s_woodStairs = new(() => Planks);
    private static readonly StairsBehavior s_cobbleStairs = new(() => Cobblestone);
    private static readonly DoorBehavior s_woodDoor = new(Material.Wood);
    private static readonly DoorBehavior s_ironDoor = new(Material.Metal);
    private static readonly TrapDoorBehavior s_trapDoor = new(Material.Wood);
    private static readonly RailBehavior s_normalRail = new(false);
    private static readonly RailBehavior s_poweredRail = new(true);
    private static readonly CropBehavior s_cropBehavior = new();
    private static readonly FarmlandBehavior s_farmlandBehavior = new();
    private static readonly ReedBehavior s_reedBehavior = new();
    private static readonly CactusBehavior s_cactusBehavior = new();
    private static readonly MushroomBehavior s_mushroomBehavior = new();
    private static readonly LeavesBehavior s_leavesBehavior = new();
    private static readonly FenceBehavior s_fenceBehavior = new();
    private static readonly SnowBehavior s_snowBehavior = new();
    private static readonly WallMountBehavior s_torchBehavior = new(false);
    private static readonly WallMountBehavior s_ladderBehavior = new(true);
    private static readonly RedstoneTorchBehavior s_redstoneTorchBehavior = new(s_torchBehavior);
    private static readonly SaplingBehavior s_saplingBehavior = new();
    private static readonly TallGrassBehavior s_tallGrassBehavior = new();
    private static readonly SoulSandBehavior s_soulSandBehavior = new();
    private static readonly WebBehavior s_webBehavior = new();
    private static readonly FireBehavior s_fireBehavior = new();
    private static readonly BedBehavior s_bedBehavior = new();
    private static readonly CakeBehavior s_cakeBehavior = new();
    private static readonly TNTBehavior s_tntBehavior = new();
    private static readonly PumpkinBehavior s_pumpkinBehavior = new(false);
    private static readonly PumpkinBehavior s_jackLanternBehavior = new(true);
    private static readonly PortalBehavior s_portalBehavior = new();
    private static readonly PistonBaseBehavior s_pistonBaseBehavior = new(false);
    private static readonly PistonBaseBehavior s_stickyPistonBaseBehavior = new(true);
    private static readonly PistonExtensionBehavior s_pistonExtensionBehavior = new();
    private static readonly PistonMovingBehavior s_pistonMovingBehavior = new();
    private static readonly FlowingFluidBehavior s_flowingFluidBehavior = new();
    private static readonly StationaryFluidBehavior s_stationaryFluidBehavior = new();

    public static readonly Block Stone = new Block(1, BlockTextures.Stone, Material.Stone)
        .setLootTable(new LootTable(new LootEntry(() => Cobblestone.id)))
        .setHardness(1.5F)
        .setResistance(10.0F)
        .setSoundGroup(SoundStoneFootstep)
        .setBlockName("stone");
    public static readonly Block GrassBlock = new Block(2, BlockTextures.GrassSide, Material.SolidOrganic)
        .setTopBottomTextures(BlockTextures.GrassTop, BlockTextures.Dirt)
        .SetVisuals(new GrassVisualBehavior())
        .SetTicker(new GrassTickerBehavior())
        .setTickRandomly(true)
        .setLootTable(new LootTable(new LootEntry(() => Dirt.id)))
        .setHardness(0.6F)
        .setSoundGroup(SoundGrassFootstep)
        .setBlockName("grass")
        .SetVariance(TextureVariance.Rotations, TextureVariance.All, TextureVariance.None);
    public static readonly Block Dirt = new Block(3, BlockTextures.Dirt, Material.Soil).setHardness(0.5F).setSoundGroup(SoundGravelFootstep).setBlockName("dirt").SetVariance(TextureVariance.All);
    public static readonly Block Cobblestone = new Block(4, BlockTextures.Cobblestone, Material.Stone).setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("stonebrick");
    public static readonly Block Planks = new Block(5, BlockTextures.OakPlanks, Material.Wood).setHardness(2.0F).setResistance(5.0F).setSoundGroup(SoundWoodFootstep).setBlockName("wood").IgnoreMetaUpdates();
    public static readonly Block Sapling = new Block(6, BlockTextures.SaplingOak, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.5F - 0.4F, 0.0F, 0.5F - 0.4F, 0.5F + 0.4F, 0.8F, 0.5F + 0.4F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_saplingBehavior).SetPhysics(s_plantSurvival).SetVisuals(s_saplingBehavior).SetLifecycle(s_saplingBehavior)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("sapling").IgnoreMetaUpdates();

    public static readonly Block Bedrock = new Block(7, BlockTextures.Bedrock, Material.Stone).setUnbreakable().setResistance(6000000.0F).setSoundGroup(SoundStoneFootstep).setBlockName("bedrock").disableStats()
        .SetVariance(TextureVariance.All);

    public static readonly Block FlowingWater = new Block(8, (Material.Water == Material.Lava ? 14 : 12) * 16 + 13, Material.Water)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).setTickRandomly(true).setTickRate(5)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_flowingFluidBehavior).SetVisuals(s_flowingFluidBehavior).SetLifecycle(s_flowingFluidBehavior).SetTicker(s_flowingFluidBehavior)
        .setDropCount(0).setRenderLayer(1)
        .setHardness(100.0F).setOpacity(3).setBlockName("water").disableStats().IgnoreMetaUpdates();
    public static readonly Block Water = new Block(9, (Material.Water == Material.Lava ? 14 : 12) * 16 + 13, Material.Water)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).setTickRandomly(false).setTickRate(5)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_stationaryFluidBehavior).SetVisuals(s_stationaryFluidBehavior).SetLifecycle(s_stationaryFluidBehavior).SetTicker(s_stationaryFluidBehavior)
        .setDropCount(0).setRenderLayer(1)
        .setHardness(100.0F).setOpacity(3).setBlockName("water").disableStats().IgnoreMetaUpdates();
    public static readonly Block FlowingLava = new Block(10, (Material.Lava == Material.Lava ? 14 : 12) * 16 + 13, Material.Lava)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).setTickRandomly(true).setTickRate(30)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_flowingFluidBehavior).SetVisuals(s_flowingFluidBehavior).SetLifecycle(s_flowingFluidBehavior).SetTicker(s_flowingFluidBehavior)
        .setDropCount(0)
        .setHardness(0.0F).setLuminance(1.0F).setOpacity(255).setBlockName("lava").disableStats().IgnoreMetaUpdates();
    public static readonly Block Lava = new Block(11, (Material.Lava == Material.Lava ? 14 : 12) * 16 + 13, Material.Lava)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).setTickRandomly(true).setTickRate(30)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_stationaryFluidBehavior).SetVisuals(s_stationaryFluidBehavior).SetLifecycle(s_stationaryFluidBehavior).SetTicker(s_stationaryFluidBehavior)
        .setDropCount(0)
        .setHardness(100.0F).setLuminance(1.0F).setOpacity(255).setBlockName("lava").disableStats().IgnoreMetaUpdates();
    public static readonly Block Sand = new Block(12, BlockTextures.Sand, Material.Sand)
        .SetTicker(s_fallingBehavior).SetLifecycle(s_fallingBehavior).SetPhysics(s_fallingBehavior).setTickRate(3)
        .setHardness(0.5F).setSoundGroup(SoundSandFootstep).setBlockName("sand").SetVariance(TextureVariance.Rotations);
    public static readonly Block Gravel = new Block(13, BlockTextures.Gravel, Material.Sand)
        .SetTicker(s_fallingBehavior).SetLifecycle(s_fallingBehavior).SetPhysics(s_fallingBehavior).setTickRate(3)
        .setLootTable(new LootTable(new LootEntry(() => Gravel.id, weight: 9), new LootEntry(() => Item.ByName("flint").Id, weight: 1)))
        .setHardness(0.6F).setSoundGroup(SoundGravelFootstep).setBlockName("gravel").SetVariance(TextureVariance.Rotations);

    public static readonly Block GoldOre = new Block(14, BlockTextures.GoldOre, Material.Stone).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreGold")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block IronOre = new Block(15, BlockTextures.IronOre, Material.Stone).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreIron")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block CoalOre = new Block(16, BlockTextures.CoalOre, Material.Stone).setLootTable(new LootTable(new LootEntry(() => Item.ByName("coal").Id))).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreCoal")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block Log = new Block(17, BlockTextures.LogOakSide, Material.Wood)
        .SetVisuals(s_logBehavior).SetLifecycle(s_logBehavior).preserveMetaOnDrop()
        .setHardness(2.0F).setSoundGroup(SoundWoodFootstep).setBlockName("log").IgnoreMetaUpdates().SetVariance(TextureVariance.All, TextureVariance.Rotate180);

    // isOpaque() is cached into BlocksOpaque[id] before SetVisuals runs, so it snapshots the base
    // default (true) here — matching original BlockLeavesBase(..., graphicsLevel: false). The live
    // isOpaque() call (used for rendering) toggles dynamically via LeavesBehavior once Visuals is set.
    public static readonly Block Leaves = new Block(18, BlockTextures.LeavesOak, Material.Leaves)
        .SetTicker(s_leavesBehavior).SetLifecycle(s_leavesBehavior).SetVisuals(s_leavesBehavior)
        .setHardness(0.2F).setOpacity(1).setSoundGroup(SoundGrassFootstep).setBlockName("leaves").disableStats().IgnoreMetaUpdates()
        .SetVariance(TextureVariance.All, TextureVariance.Rotate180);

    public static readonly Block Sponge = new Block(19, BlockTextures.Sponge, Material.Sponge)
        .SetLifecycle(new SpongeLifecycleBehavior())
        .setHardness(0.6F).setSoundGroup(SoundGrassFootstep).setBlockName("sponge").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);
    public static readonly Block Glass = new Block(20, BlockTextures.Glass, Material.Glass).setNonOpaque().setDropCount(0).SetVisuals(new GlassVisualBehavior(false))
        .setHardness(0.3F).setSoundGroup(SoundGlassFootstep).setBlockName("glass").SetVariance(TextureVariance.Rotate180);

    public static readonly Block LapisOre = new Block(21, BlockTextures.LapisOre, Material.Stone).setLootTable(new LootTable(new LootEntry(() => Item.ByName("dye_powder").Id)), 4, 8, 4).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreLapis")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block LapisBlock = new Block(22, BlockTextures.BlockLapis, Material.Stone).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("blockLapis");
    public static readonly Block Dispenser = new Block(23, BlockTextures.FurnaceSide, Material.Stone)
        .setHasTileEntity(() => new BlockEntityDispenser())
        .SetInteractable(s_dispenserBehavior).SetLifecycle(s_dispenserBehavior).SetPhysics(s_dispenserBehavior).SetTicker(s_dispenserBehavior).SetVisuals(s_dispenserBehavior)
        .setTickRate(4)
        .setHardness(3.5F).setSoundGroup(SoundStoneFootstep).setBlockName("dispenser").IgnoreMetaUpdates();
    public static readonly Block Sandstone = new Block(24, BlockTextures.SandstoneSide, Material.Stone).setTopBottomTextures(BlockTextures.SandstoneTop, BlockTextures.SandstoneBottom).setSoundGroup(SoundStoneFootstep).setHardness(0.8F).setBlockName("sandStone").SetVariance(TextureVariance.Rotations, TextureVariance.None);
    public static readonly Block Noteblock = new Block(25, BlockTextures.NoteBlock, Material.Wood)
        .setHasTileEntity(() => new BlockEntityNote())
        .SetInteractable(s_noteblockBehavior).SetLifecycle(s_noteblockBehavior).SetPhysics(s_noteblockBehavior)
        .setHardness(0.8F).setBlockName("musicBlock").IgnoreMetaUpdates();
    public static readonly Block Bed = new Block(26, BlockTextures.BedTopFoot, Material.Wool)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 9.0F / 16.0F, 1.0F)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Bed).setPistonBehavior(PistonBehavior.Destroy)
        .SetInteractable(s_bedBehavior).SetPhysics(s_bedBehavior).SetLifecycle(s_bedBehavior).SetVisuals(s_bedBehavior)
        .setHardness(0.2F).setBlockName("bed").disableStats().IgnoreMetaUpdates();
    public static readonly Block PoweredRail = new Block(27, BlockTextures.PoweredRailOn, Material.PistonBreakable)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.MinecartTrack).setNoCollision()
        .setPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_poweredRail).SetLifecycle(s_poweredRail).SetVisuals(s_poweredRail)
        .setHardness(0.7F).setSoundGroup(SoundMetalFootstep).setBlockName("goldenRail").IgnoreMetaUpdates();
    public static readonly Block DetectorRail = new Block(28, BlockTextures.DetectorRail, Material.PistonBreakable)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.MinecartTrack).setNoCollision()
        .setPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_poweredRail).SetLifecycle(s_poweredRail).SetVisuals(s_poweredRail)
        .setTickRandomly(true).setTickRate(20)
        .SetRedstone(s_detectorRail).SetTicker(s_detectorRail).SetInteractable(s_detectorRail)
        .setHardness(0.7F).setSoundGroup(SoundMetalFootstep).setBlockName("detectorRail").IgnoreMetaUpdates();
    public static readonly Block StickyPiston = new Block(29, BlockTextures.PistonTopSticky, Material.Piston)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.PistonBase)
        .SetPhysics(s_stickyPistonBaseBehavior).SetLifecycle(s_stickyPistonBaseBehavior).SetTicker(s_stickyPistonBaseBehavior).SetVisuals(s_stickyPistonBaseBehavior)
        .setSoundGroup(SoundStoneFootstep).setHardness(0.5F).setBlockName("pistonStickyBase").IgnoreMetaUpdates();
    public static readonly Block Cobweb = new Block(30, BlockTextures.Cobweb, Material.Cobweb)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetInteractable(s_webBehavior).setLootTable(new LootTable(new LootEntry(() => Item.ByName("string").Id)))
        .setOpacity(1).setHardness(4.0F).setBlockName("web");
    public static readonly Block Grass = new Block(31, BlockTextures.TallGrass, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.5F - 0.4F, 0.0F, 0.5F - 0.4F, 0.5F + 0.4F, 0.8F, 0.5F + 0.4F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival).SetVisuals(s_tallGrassBehavior).SetLifecycle(s_tallGrassBehavior)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("tallgrass");
    public static readonly Block DeadBush = new Block(32, BlockTextures.DeadBush, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.1F, 0.0F, 0.1F, 0.9F, 0.8F, 0.9F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_deadBushSurvival).SetPhysics(s_deadBushSurvival).setDropCount(0)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("deadbush");
    public static readonly Block Piston = new Block(33, BlockTextures.PistonTopNormal, Material.Piston)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.PistonBase)
        .SetPhysics(s_pistonBaseBehavior).SetLifecycle(s_pistonBaseBehavior).SetTicker(s_pistonBaseBehavior).SetVisuals(s_pistonBaseBehavior)
        .setSoundGroup(SoundStoneFootstep).setHardness(0.5F).setBlockName("pistonBase").IgnoreMetaUpdates();
    public static readonly Block PistonHead = new Block(34, BlockTextures.PistonTopNormal, Material.Piston)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.PistonExtension).setDropCount(0)
        .SetPhysics(s_pistonExtensionBehavior).SetLifecycle(s_pistonExtensionBehavior).SetVisuals(s_pistonExtensionBehavior)
        .setSoundGroup(SoundStoneFootstep).setHardness(0.5F).IgnoreMetaUpdates();
    public static readonly Block Wool = new Block(35, 64, Material.Wool).SetVisuals(new ClothVisualBehavior()).preserveMetaOnDrop()
        .setBlockAlias(
            "blackWool:15", "redWool:14", "greenWool:13", "brownWool:12",
            "blueWool:11", "purpleWool:10", "cyanWool:9", "silverWool:8",
            "grayWool:7", "pinkWool:6", "limeWool:5", "yellowWool:4",
            "lightBlueWool:3", "magentaWool:2", "orangeWool:1", "whiteWool:0")
        .setHardness(0.8F).setSoundGroup(SoundClothFootstep).setBlockName("cloth").IgnoreMetaUpdates().SetVariance(TextureVariance.None, TextureVariance.FlipBoth);
    public static readonly Block MovingPiston = new Block(36, Material.Piston)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Entity)
        .SetPhysics(s_pistonMovingBehavior).SetLifecycle(s_pistonMovingBehavior).SetInteractable(s_pistonMovingBehavior)
        .setHardness(-1.0F);
    public static readonly Block Dandelion = new Block(37, BlockTextures.Dandelion, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.6F, 0.7F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("flower");
    public static readonly Block Rose = new Block(38, BlockTextures.Rose, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.6F, 0.7F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("rose");
    public static readonly Block BrownMushroom = new Block(39, BlockTextures.BrownMushroom, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.4F, 0.7F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_mushroomBehavior).SetPhysics(s_mushroomBehavior)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setLuminance(2.0F / 16.0F).setBlockName("mushroom");
    public static readonly Block RedMushroom = new Block(40, BlockTextures.RedMushroom, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.4F, 0.7F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_mushroomBehavior).SetPhysics(s_mushroomBehavior)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("mushroom");
    public static readonly Block GoldBlock = new Block(41, BlockTextures.BlockGold, Material.Metal).setHardness(3.0F).setResistance(10.0F).setSoundGroup(SoundMetalFootstep).setBlockName("blockGold");
    public static readonly Block IronBlock = new Block(42, BlockTextures.BlockIron, Material.Metal).setHardness(5.0F).setResistance(10.0F).setSoundGroup(SoundMetalFootstep).setBlockName("blockIron");
    public static readonly Block DoubleSlab = new Block(43, BlockTextures.StoneSlabTop, Material.Stone)
        .SetPhysics(s_doubleSlab).SetLifecycle(s_doubleSlab).SetVisuals(s_doubleSlab)
        .setLootTable(new LootTable(new LootEntry(() => Slab.id))).setDropCount(2).preserveMetaOnDrop()
        .setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("stoneSlab");
    public static readonly Block Slab = new Block(44, BlockTextures.StoneSlabTop, Material.Stone)
        .setNonOpaque().setNotFullCube().setOpacity(255)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F)
        .SetPhysics(s_singleSlab).SetLifecycle(s_singleSlab).SetVisuals(s_singleSlab)
        .setLootTable(new LootTable(new LootEntry(() => Slab.id))).preserveMetaOnDrop()
        .setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("stoneSlab");
    public static readonly Block Bricks = new Block(45, BlockTextures.Bricks, Material.Stone).setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("brick");
    public static readonly Block TNT = new Block(46, BlockTextures.TntSide, Material.Tnt)
        .SetPhysics(s_tntBehavior).SetLifecycle(s_tntBehavior).SetInteractable(s_tntBehavior).SetVisuals(s_tntBehavior)
        .setDropCount(0)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("tnt");
    public static readonly Block Bookshelf = new Block(47, BlockTextures.Bookshelf, Material.Wood).setTopBottomTextures(BlockTextures.OakPlanks, BlockTextures.OakPlanks).setDropCount(0).setHardness(1.5F).setSoundGroup(SoundWoodFootstep).setBlockName("bookshelf").SetVariance(TextureVariance.None, TextureVariance.FlipU);
    public static readonly Block MossyCobblestone = new Block(48, BlockTextures.MossyCobblestone, Material.Stone).setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("stoneMoss");
    public static readonly Block Obsidian = new Block(49, BlockTextures.Obsidian, Material.Stone).setHardness(10.0F).setResistance(2000.0F).setSoundGroup(SoundStoneFootstep).setBlockName("obsidian");
    public static readonly Block Torch = new Block(50, BlockTextures.Torch, Material.PistonBreakable)
        .setTickRandomly(true).setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Torch)
        .SetPhysics(s_torchBehavior).SetLifecycle(s_torchBehavior).SetTicker(s_torchBehavior)
        .setHardness(0.0F).setLuminance(15.0F / 16.0F).setSoundGroup(SoundWoodFootstep).setBlockName("torch").IgnoreMetaUpdates();
    public static readonly Block Fire = new Block(51, BlockTextures.Fire, Material.Fire)
        .setTickRandomly(true).setTickRate(40).setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Fire)
        .SetTicker(s_fireBehavior).SetPhysics(s_fireBehavior).SetLifecycle(s_fireBehavior)
        .setDropCount(0)
        .setHardness(0.0F).setLuminance(1.0F).setSoundGroup(SoundWoodFootstep).setBlockName("fire").disableStats().IgnoreMetaUpdates();
    public static readonly Block Spawner = new Block(52, BlockTextures.Spawner, Material.Stone)
        .setHasTileEntity(() => new BlockEntityMobSpawner())
        .SetLifecycle(s_tileEntityLifecycle)
        .setDropCount(0).setNonOpaque()
        .setHardness(5.0F).setSoundGroup(SoundMetalFootstep).setBlockName("mobSpawner").disableStats();
    public static readonly Block WoodenStairs = new Block(53, Planks.TextureId, Planks.material)
        .setHardness(Planks.hardness).setResistance(Planks.resistance / 3.0F).setSoundGroup(Planks.SoundGroup)
        .setOpacity(255).setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Stairs)
        .SetPhysics(s_woodStairs).SetLifecycle(s_woodStairs).SetVisuals(s_woodStairs)
        .setBlockName("stairsWood").IgnoreMetaUpdates();
    public static readonly Block Chest = new Block(54, BlockTextures.ChestSingleSide, Material.Wood)
        .setHasTileEntity(() => new BlockEntityChest())
        .SetInteractable(s_chestBehavior).SetLifecycle(s_chestBehavior).SetPhysics(s_chestBehavior).SetVisuals(s_chestBehavior)
        .setHardness(2.5F).setSoundGroup(SoundWoodFootstep).setBlockName("chest").IgnoreMetaUpdates();
    public static readonly Block RedstoneWire = new Block(55, BlockTextures.RedstoneWireCross, Material.PistonBreakable)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F / 16.0F, 1.0F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.RedstoneWire)
        .SetRedstone(s_redstoneWire).SetPhysics(s_redstoneWire).SetTicker(s_redstoneWire).SetLifecycle(s_redstoneWire).SetVisuals(s_redstoneWire)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)))
        .setHardness(0.0F).setSoundGroup(SoundPowderFootstep).setBlockName("redstoneDust").disableStats().IgnoreMetaUpdates();

    public static readonly Block DiamondOre = new Block(56, BlockTextures.DiamondOre, Material.Stone).setLootTable(new LootTable(new LootEntry(() => Item.ByName("diamond").Id))).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreDiamond")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block DiamondBlock = new Block(57, BlockTextures.BlockDiamond, Material.Metal).setHardness(5.0F).setResistance(10.0F).setSoundGroup(SoundMetalFootstep).setBlockName("blockDiamond");
    public static readonly Block CraftingTable = new Block(58, BlockTextures.CraftingTableSide, Material.Wood).setTopBottomTextures(BlockTextures.CraftingTableTop, BlockTextures.OakPlanks)
        .setFaceTexture(Side.North, BlockTextures.CraftingTableFront).setFaceTexture(Side.West, BlockTextures.CraftingTableFront)
        .setHardness(2.5F).setSoundGroup(SoundWoodFootstep).setBlockName("workbench").SetInteractable(new WorkbenchInteractBehavior());
    public static readonly Block Wheat = new Block(59, BlockTextures.WheatStageBase, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.25F, 1.0F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Crops)
        .SetTicker(s_cropBehavior).SetPhysics(s_cropBehavior).SetLifecycle(s_cropBehavior).SetVisuals(s_cropBehavior)
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("crops").disableStats().IgnoreMetaUpdates();
    public static readonly Block Farmland = new Block(60, BlockTextures.FarmlandDry, Material.Soil)
        .setTickRandomly(true).setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 15.0F / 16.0F, 1.0F)
        .setNonOpaque().setNotFullCube().setOpacity(255)
        .SetTicker(s_farmlandBehavior).SetPhysics(s_farmlandBehavior).SetInteractable(s_farmlandBehavior).SetLifecycle(s_farmlandBehavior).SetVisuals(s_farmlandBehavior)
        .setHardness(0.6F).setSoundGroup(SoundGravelFootstep).setBlockName("farmland");
    public static readonly Block Furnace = new Block(61, BlockTextures.FurnaceSide, Material.Stone)
        .setHasTileEntity(() => new BlockEntityFurnace())
        .SetInteractable(s_furnaceUnlit).SetLifecycle(s_furnaceUnlit).SetPhysics(s_furnaceUnlit).SetTicker(s_furnaceUnlit).SetVisuals(s_furnaceUnlit)
        .setHardness(3.5F).setSoundGroup(SoundStoneFootstep).setBlockName("furnace").IgnoreMetaUpdates();
    public static readonly Block LitFurnace = new Block(62, BlockTextures.FurnaceSide, Material.Stone)
        .setHasTileEntity(() => new BlockEntityFurnace())
        .SetInteractable(s_furnaceLit).SetLifecycle(s_furnaceLit).SetPhysics(s_furnaceLit).SetTicker(s_furnaceLit).SetVisuals(s_furnaceLit)
        .setLootTable(new LootTable(new LootEntry(() => Furnace.id)))
        .setHardness(3.5F).setSoundGroup(SoundStoneFootstep).setLuminance(14.0F / 16.0F).setBlockName("furnace").IgnoreMetaUpdates();
    public static readonly Block Sign = new Block(63, BlockTextures.OakPlanks, Material.Wood)
        .setHasTileEntity(() => new BlockEntitySign())
        .setBoundingBox(0.25F, 0.0F, 0.25F, 0.75F, 1.0F, 0.75F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Entity)
        .SetPhysics(s_standingSign).SetLifecycle(s_tileEntityLifecycle)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("sign").Id)))
        .setHardness(1.0F).setSoundGroup(SoundWoodFootstep).setBlockName("sign").disableStats().IgnoreMetaUpdates();
    public static readonly Block Door = new Block(64, BlockTextures.DoorWood, Material.Wood)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Door)
        .setPistonBehavior(PistonBehavior.Destroy)
        .SetPhysics(s_woodDoor).SetInteractable(s_woodDoor).SetLifecycle(s_woodDoor).SetVisuals(s_woodDoor)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("door_wood").Id)))
        .setHardness(3.0F).setSoundGroup(SoundWoodFootstep).setBlockName("doorWood").disableStats().IgnoreMetaUpdates();
    public static readonly Block Ladder = new Block(65, BlockTextures.Ladder, Material.PistonBreakable)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Ladder)
        .SetPhysics(s_ladderBehavior).SetLifecycle(s_ladderBehavior)
        .setHardness(0.4F).setSoundGroup(SoundWoodFootstep).setBlockName("ladder").IgnoreMetaUpdates();
    public static readonly Block Rail = new Block(66, BlockTextures.RailStraight, Material.PistonBreakable)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.MinecartTrack).setNoCollision()
        .setPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_normalRail).SetLifecycle(s_normalRail).SetVisuals(s_normalRail)
        .setHardness(0.7F).setSoundGroup(SoundMetalFootstep).setBlockName("rail").IgnoreMetaUpdates();
    public static readonly Block CobblestoneStairs = new Block(67, Cobblestone.TextureId, Cobblestone.material)
        .setHardness(Cobblestone.hardness).setResistance(Cobblestone.resistance / 3.0F).setSoundGroup(Cobblestone.SoundGroup)
        .setOpacity(255).setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Stairs)
        .SetPhysics(s_cobbleStairs).SetLifecycle(s_cobbleStairs).SetVisuals(s_cobbleStairs)
        .setBlockName("stairsStone").IgnoreMetaUpdates();
    public static readonly Block WallSign = new Block(68, BlockTextures.OakPlanks, Material.Wood)
        .setHasTileEntity(() => new BlockEntitySign())
        .setBoundingBox(0.25F, 0.0F, 0.25F, 0.75F, 1.0F, 0.75F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Entity)
        .SetPhysics(s_wallSign).SetLifecycle(s_tileEntityLifecycle)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("sign").Id)))
        .setHardness(1.0F).setSoundGroup(SoundWoodFootstep).setBlockName("sign").disableStats().IgnoreMetaUpdates();
    public static readonly Block Lever = new Block(69, BlockTextures.Lever, Material.PistonBreakable)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Lever)
        .SetRedstone(s_leverBehavior).SetPhysics(s_leverBehavior).SetInteractable(s_leverBehavior).SetLifecycle(s_leverBehavior)
        .setHardness(0.5F).setSoundGroup(SoundWoodFootstep).setBlockName("lever").IgnoreMetaUpdates();

    public static readonly Block StonePressurePlate = new Block(70, BlockTextures.Stone, Material.Stone)
        .setTickRandomly(true).setTickRate(20)
        .setBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 15.0F / 16.0F, 1.0F / 32.0F, 15.0F / 16.0F)
        .setNonOpaque().setNotFullCube().setNoCollision().setPistonBehavior(PistonBehavior.Destroy)
        .SetRedstone(s_mobPlate).SetPhysics(s_mobPlate).SetTicker(s_mobPlate).SetInteractable(s_mobPlate).SetLifecycle(s_mobPlate)
        .setHardness(0.5F).setSoundGroup(SoundStoneFootstep).setBlockName("pressurePlate")
        .IgnoreMetaUpdates();

    public static readonly Block IronDoor = new Block(71, BlockTextures.DoorIron, Material.Metal)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Door)
        .setPistonBehavior(PistonBehavior.Destroy)
        .SetPhysics(s_ironDoor).SetInteractable(s_ironDoor).SetLifecycle(s_ironDoor).SetVisuals(s_ironDoor)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("door_iron").Id)))
        .setHardness(5.0F).setSoundGroup(SoundMetalFootstep).setBlockName("doorIron").disableStats().IgnoreMetaUpdates();

    public static readonly Block WoodenPressurePlate = new Block(72, BlockTextures.OakPlanks, Material.Wood)
        .setTickRandomly(true).setTickRate(20)
        .setBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 15.0F / 16.0F, 1.0F / 32.0F, 15.0F / 16.0F)
        .setNonOpaque().setNotFullCube().setNoCollision().setPistonBehavior(PistonBehavior.Destroy)
        .SetRedstone(s_everythingPlate).SetPhysics(s_everythingPlate).SetTicker(s_everythingPlate).SetInteractable(s_everythingPlate).SetLifecycle(s_everythingPlate)
        .setHardness(0.5F).setSoundGroup(SoundWoodFootstep)
        .setBlockName("pressurePlate")
        .IgnoreMetaUpdates();

    public static readonly Block RedstoneOre = new Block(73, BlockTextures.RedstoneOre, Material.Stone)
        .setTickRate(30)
        .SetTicker(s_redstoneOreBehavior).SetInteractable(s_redstoneOreBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)), 4, 5)
        .setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreRedstone").IgnoreMetaUpdates()
        .SetVariance(TextureVariance.Rotate180, TextureVariance.FlipBoth);

    public static readonly Block LitRedstoneOre = new Block(74, BlockTextures.RedstoneOre, Material.Stone)
        .setTickRandomly(true).setTickRate(30)
        .SetTicker(s_redstoneOreBehavior).SetInteractable(s_redstoneOreBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)), 4, 5)
        .setLuminance(10.0F / 16.0F).setHardness(3.0F).setResistance(5.0F).setSoundGroup(SoundStoneFootstep).setBlockName("oreRedstone")
        .IgnoreMetaUpdates()
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block RedstoneTorch = new Block(75, BlockTextures.RedstoneTorchUnlit, Material.PistonBreakable)
        .setTickRandomly(true).setTickRate(2).setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Torch)
        .SetPhysics(s_redstoneTorchBehavior).SetLifecycle(s_redstoneTorchBehavior).SetRedstone(s_redstoneTorchBehavior).SetTicker(s_redstoneTorchBehavior).SetVisuals(s_redstoneTorchBehavior)
        .setLootTable(new LootTable(new LootEntry(() => LitRedstoneTorch.id)))
        .setHardness(0.0F).setSoundGroup(SoundWoodFootstep).setBlockName("notGate").IgnoreMetaUpdates();
    public static readonly Block LitRedstoneTorch = new Block(76, BlockTextures.RedstoneTorchLit, Material.PistonBreakable)
        .setTickRandomly(true).setTickRate(2).setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Torch)
        .SetPhysics(s_redstoneTorchBehavior).SetLifecycle(s_redstoneTorchBehavior).SetRedstone(s_redstoneTorchBehavior).SetTicker(s_redstoneTorchBehavior).SetVisuals(s_redstoneTorchBehavior)
        .setLootTable(new LootTable(new LootEntry(() => LitRedstoneTorch.id)))
        .setHardness(0.0F).setLuminance(0.5F).setSoundGroup(SoundWoodFootstep).setBlockName("notGate").IgnoreMetaUpdates();
    public static readonly Block Button = new Block(77, BlockTextures.Stone, Material.PistonBreakable)
        .setTickRandomly(true).setTickRate(20)
        .setNonOpaque().setNotFullCube().setNoCollision()
        .SetRedstone(s_buttonBehavior).SetPhysics(s_buttonBehavior).SetTicker(s_buttonBehavior).SetInteractable(s_buttonBehavior).SetLifecycle(s_buttonBehavior)
        .setHardness(0.5F).setSoundGroup(SoundStoneFootstep).setBlockName("button").IgnoreMetaUpdates();
    public static readonly Block Snow = new Block(78, BlockTextures.Snow, Material.SnowLayer)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F).setTickRandomly(true)
        .setNonOpaque().setNotFullCube()
        .SetPhysics(s_snowBehavior).SetTicker(s_snowBehavior).SetLifecycle(s_snowBehavior).SetVisuals(s_snowBehavior)
        .setHardness(0.1F).setSoundGroup(SoundClothFootstep).setBlockName("snow").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);
    public static readonly Block Ice = new Block(79, BlockTextures.Ice, Material.Ice)
        .setTickRandomly(true).setNonOpaque().setRenderLayer(1).setSlipperiness(0.98F).setDropCount(0)
        .SetVisuals(new GlassVisualBehavior(false)).SetTicker(s_iceMelt).SetLifecycle(s_iceMelt)
        .setHardness(0.5F).setOpacity(3).setSoundGroup(SoundGlassFootstep).setBlockName("ice").SetVariance(TextureVariance.Rotate180);
    public static readonly Block SnowBlock = new Block(80, BlockTextures.Snow, Material.SnowBlock)
        .setTickRandomly(true).SetTicker(s_snowMelt).setLootTable(new LootTable(new LootEntry(() => Item.ByName("snowball").Id)), 4)
        .setHardness(0.2F).setSoundGroup(SoundClothFootstep).setBlockName("snow").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block Cactus = new Block(81, BlockTextures.CactusSide, Material.Cactus)
        .setTickRandomly(true).setBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 1.0F - 1.0F / 16.0F, 1.0F, 1.0F - 1.0F / 16.0F)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Cactus)
        .SetTicker(s_cactusBehavior).SetPhysics(s_cactusBehavior).SetInteractable(s_cactusBehavior).SetVisuals(s_cactusBehavior)
        .setHardness(0.4F).setSoundGroup(SoundClothFootstep).setBlockName("cactus")
        .SetVariance(TextureVariance.All, TextureVariance.All, TextureVariance.Rotate180);

    public static readonly Block Clay = new Block(82, BlockTextures.Clay, Material.Clay).setLootTable(new LootTable(new LootEntry(() => Item.ByName("clay").Id)), 4).setHardness(0.6F).setSoundGroup(SoundGravelFootstep).setBlockName("clay").SetVariance(TextureVariance.Rotations);
    public static readonly Block SugarCane = new Block(83, BlockTextures.SugarCane, Material.Plant)
        .setTickRandomly(true).setBoundingBox(0.5F - 6.0F / 16.0F, 0.0F, 0.5F - 6.0F / 16.0F, 0.5F + 6.0F / 16.0F, 1.0F, 0.5F + 6.0F / 16.0F)
        .setNonOpaque().setNotFullCube().setNoCollision().setRenderType(BlockRendererType.Reed)
        .SetTicker(s_reedBehavior).SetPhysics(s_reedBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("sugar_canes").Id)))
        .setHardness(0.0F).setSoundGroup(SoundGrassFootstep).setBlockName("reeds").disableStats();
    public static readonly Block Jukebox = new Block(84, BlockTextures.NoteBlock, Material.Wood)
        .setHasTileEntity(() => new BlockEntityRecordPlayer())
        .setFaceTexture(Side.Up, BlockTextures.JukeboxTop)
        .SetInteractable(s_jukeboxBehavior).SetLifecycle(s_jukeboxBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Jukebox.id)))
        .setHardness(2.0F).setResistance(10.0F).setSoundGroup(SoundStoneFootstep).setBlockName("jukebox").IgnoreMetaUpdates();
    public static readonly Block Fence = new Block(85, BlockTextures.OakPlanks, Material.Wood)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Fence)
        .SetPhysics(s_fenceBehavior)
        .setHardness(2.0F).setResistance(5.0F).setSoundGroup(SoundWoodFootstep).setBlockName("fence").IgnoreMetaUpdates();
    public static readonly Block Pumpkin = new Block(86, BlockTextures.PumpkinBase, Material.Pumpkin)
        .setTickRandomly(true)
        .SetPhysics(s_pumpkinBehavior).SetLifecycle(s_pumpkinBehavior).SetVisuals(s_pumpkinBehavior)
        .setHardness(1.0F).setSoundGroup(SoundWoodFootstep).setBlockName("pumpkin").IgnoreMetaUpdates();
    public static readonly Block Netherrack = new Block(87, BlockTextures.Netherrack, Material.Stone).setHardness(0.4F).setSoundGroup(SoundStoneFootstep).setBlockName("hellrock").SetVariance(TextureVariance.All);
    public static readonly Block Soulsand = new Block(88, BlockTextures.SoulSand, Material.Sand)
        .SetPhysics(s_soulSandBehavior).SetInteractable(s_soulSandBehavior)
        .setHardness(0.5F).setSoundGroup(SoundSandFootstep).setBlockName("hellsand");

    public static readonly Block Glowstone = new Block(89, BlockTextures.Glowstone, Material.Stone).setLootTable(new LootTable(new LootEntry(() => Item.ByName("yellow_dust").Id)), 2, 4).setHardness(0.3F).setSoundGroup(SoundGlassFootstep).setLuminance(1.0F).setBlockName("lightgem")
        .SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block NetherPortal = new Block(90, BlockTextures.Portal, Material.NetherPortal)
        .setNonOpaque().setNotFullCube().setRenderLayer(1)
        .SetPhysics(s_portalBehavior).SetVisuals(s_portalBehavior).SetInteractable(s_portalBehavior).SetTicker(s_portalBehavior)
        .setDropCount(0)
        .setHardness(-1.0F).setSoundGroup(SoundGlassFootstep).setLuminance(12.0F / 16.0F).setBlockName("portal");

    public static readonly Block JackLantern = new Block(91, BlockTextures.PumpkinBase, Material.Pumpkin)
        .setTickRandomly(true)
        .SetPhysics(s_jackLanternBehavior).SetLifecycle(s_jackLanternBehavior).SetVisuals(s_jackLanternBehavior)
        .setHardness(1.0F).setSoundGroup(SoundWoodFootstep).setLuminance(1.0F).setBlockName("litpumpkin").IgnoreMetaUpdates()
        .SetVariance(TextureVariance.All, TextureVariance.None);

    public static readonly Block Cake = new Block(92, BlockTextures.Cake, Material.Cake)
        .setTickRandomly(true).setNonOpaque().setNotFullCube()
        .SetPhysics(s_cakeBehavior).SetVisuals(s_cakeBehavior).SetInteractable(s_cakeBehavior)
        .setDropCount(0)
        .setHardness(0.5F).setSoundGroup(SoundClothFootstep).setBlockName("cake").disableStats().IgnoreMetaUpdates();
    public static readonly Block Repeater = new Block(93, 6, Material.PistonBreakable)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Repeater)
        .SetRedstone(s_repeaterBehavior).SetTicker(s_repeaterBehavior).SetPhysics(s_repeaterBehavior).SetInteractable(s_repeaterBehavior).SetLifecycle(s_repeaterBehavior).SetVisuals(s_repeaterBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone_repeater").Id)))
        .setHardness(0.0F).setSoundGroup(SoundWoodFootstep).setBlockName("diode").disableStats().IgnoreMetaUpdates();
    public static readonly Block PoweredRepeater = new Block(94, 6, Material.PistonBreakable)
        .setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F)
        .setNonOpaque().setNotFullCube().setRenderType(BlockRendererType.Repeater)
        .SetRedstone(s_repeaterBehavior).SetTicker(s_repeaterBehavior).SetPhysics(s_repeaterBehavior).SetInteractable(s_repeaterBehavior).SetLifecycle(s_repeaterBehavior).SetVisuals(s_repeaterBehavior)
        .setLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone_repeater").Id)))
        .setHardness(0.0F).setLuminance(10.0F / 16.0F).setSoundGroup(SoundWoodFootstep).setBlockName("diode").disableStats().IgnoreMetaUpdates();
    public static readonly Block Trapdoor = new Block(96, BlockTextures.TrapdoorWood, Material.Wood)
        .setNonOpaque().setNotFullCube()
        .SetPhysics(s_trapDoor).SetInteractable(s_trapDoor).SetLifecycle(s_trapDoor)
        .setHardness(3.0F).setSoundGroup(SoundWoodFootstep).setBlockName("trapdoor").disableStats().IgnoreMetaUpdates();

    public readonly int id;
    public readonly Material material;
    private string _blockName = "";
    public Box BoundingBox;
    public float hardness;
    public float particleFallSpeedModifier;
    public float resistance;
    protected bool shouldTrackStatistics;
    public float Slipperiness;
    public BlockSoundGroup SoundGroup;
    public int TextureId;
    private int?[]? _faceTextureIds;
    private LootTable? _lootTable;
    private int _minDroppedCount = 1;
    private int _maxDroppedCount = 1;
    private int _droppedItemMetaValue;
    private bool _dropsWithBlockMeta;
    private bool _isOpaque = true;
    private string[]? _blockAlias;
    private int _tickRate = 10;
    private bool _isFullCube = true;
    private bool _hasCollision = true;
    private PistonBehavior? _pistonBehaviorOverride;
    private Func<BlockEntity>? _blockEntityFactory;
    private BlockRendererType _renderType = BlockRendererType.Standard;
    private int _renderLayer;

    static Block()
    {
        Item.ITEMS[Wool.id] = new ItemCloth(Wool.id - 256).setItemName("cloth");
        Item.ITEMS[Log.id] = new ItemLog(Log.id - 256).setItemName("log");
        Item.ITEMS[Slab.id] = new ItemSlab(Slab.id - 256).setItemName("stoneSlab");
        Item.ITEMS[Sapling.id] = new ItemSapling(Sapling.id - 256).setItemName("sapling");
        Item.ITEMS[Leaves.id] = new ItemLeaves(Leaves.id - 256).setItemName("leaves");
        Item.ITEMS[Piston.id] = new ItemPiston(Piston.id - 256);
        Item.ITEMS[StickyPiston.id] = new ItemPiston(StickyPiston.id - 256);

        for (int blockId = 0; blockId < 256; ++blockId)
        {
            if (Blocks[blockId] != null && Item.ITEMS[blockId] == null)
            {
                Item.ITEMS[blockId] = new ItemBlock(blockId - 256);
                Blocks[blockId].init();
            }
        }

        BlocksAllowVision[0] = true;
    }

    protected Block(int id, Material material)
    {
        shouldTrackStatistics = true;
        SoundGroup = SoundPowderFootstep;
        particleFallSpeedModifier = 1.0F;
        Slipperiness = 0.6F;
        if (Blocks[id] != null)
        {
            throw new ArgumentException("Slot " + id + " is already occupied by " + Blocks[id] + " when adding " + this, nameof(id));
        }

        this.material = material;
        Blocks[id] = this;
        this.id = id;
        setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        BlocksOpaque[id] = isOpaque();
        BlockLightOpacity[id] = isOpaque() ? 255 : 0;
        BlocksAllowVision[id] = !material.BlocksVision;
        BlocksWithEntity[id] = false;
    }

    protected Block(int id, int textureId, Material material) : this(id, material) => TextureId = textureId;

    public TextureVariance TopVariance { get; private set; } = TextureVariance.None;
    public TextureVariance BottomVariance { get; private set; } = TextureVariance.None;
    public TextureVariance SideVariance { get; private set; } = TextureVariance.None;

    protected Block IgnoreMetaUpdates()
    {
        BlocksIgnoreMetaUpdate[id] = true;
        return this;
    }

    protected virtual void init() => Lifecycle?.OnInit(this);

    protected Block setSoundGroup(BlockSoundGroup soundGroup)
    {
        this.SoundGroup = soundGroup;
        return this;
    }

    protected Block setOpacity(int opacity)
    {
        BlockLightOpacity[id] = opacity;
        return this;
    }

    protected Block setNonOpaque()
    {
        // The constructor caches isOpaque() into these arrays before fluent setters run,
        // so they must be refreshed here.
        _isOpaque = false;
        BlocksOpaque[id] = false;
        BlockLightOpacity[id] = 0;
        return this;
    }

    protected Block setLuminance(float fractionalValue)
    {
        BlocksLightLuminance[id] = (int)(15.0F * fractionalValue);
        return this;
    }

    protected Block setResistance(float resistance)
    {
        this.resistance = resistance * 3.0F;
        return this;
    }

    protected Block setTopBottomTextures(int topTextureId, int bottomTextureId) => setFaceTexture(Side.Up, topTextureId).setFaceTexture(Side.Down, bottomTextureId);

    protected Block setFaceTexture(Side side, int textureId)
    {
        _faceTextureIds ??= new int?[6];
        _faceTextureIds[(int)side] = textureId;
        return this;
    }

    /// <summary>Configures the block's weighted drop table (e.g. gravel's flint chance). Item ids inside
    /// each <see cref="LootEntry"/> are deferred, so they may safely reference another block's or item's
    /// static field regardless of declaration order.</summary>
    protected Block setLootTable(LootTable table, int minCount = 1, int maxCount = -1, int meta = 0)
    {
        _lootTable = table;
        _minDroppedCount = minCount;
        _maxDroppedCount = maxCount < 0 ? minCount : maxCount;
        _droppedItemMetaValue = meta;
        return this;
    }

    protected Block setDropCount(int count)
    {
        _minDroppedCount = count;
        _maxDroppedCount = count;
        return this;
    }

    /// <summary>Makes drops carry the broken block's metadata (e.g. wool color) instead of a fixed value.</summary>
    protected Block preserveMetaOnDrop()
    {
        _dropsWithBlockMeta = true;
        return this;
    }

    protected Block setBlockAlias(params string[] aliases)
    {
        _blockAlias = aliases;
        return this;
    }

    public virtual bool isFullCube() => _isFullCube;

    public virtual BlockRendererType getRenderType() => _renderType;

    protected Block setNotFullCube()
    {
        _isFullCube = false;
        return this;
    }

    protected Block setRenderType(BlockRendererType renderType)
    {
        _renderType = renderType;
        return this;
    }

    /// <summary>Entities pass through this block (plants, portals, ...).</summary>
    protected Block setNoCollision()
    {
        _hasCollision = false;
        return this;
    }

    public Block SetVariance(TextureVariance allFaces)
    {
        TopVariance = allFaces;
        BottomVariance = allFaces;
        SideVariance = allFaces;
        return this;
    }

    public Block SetVariance(TextureVariance topBottom, TextureVariance sides)
    {
        TopVariance = topBottom;
        BottomVariance = topBottom;
        SideVariance = sides;
        return this;
    }

    public Block SetVariance(TextureVariance top, TextureVariance bottom, TextureVariance sides)
    {
        TopVariance = top;
        BottomVariance = bottom;
        SideVariance = sides;
        return this;
    }

    protected Block setHardness(float hardness)
    {
        this.hardness = hardness;
        if (resistance < hardness * 5.0F)
        {
            resistance = hardness * 5.0F;
        }

        return this;
    }

    protected Block setUnbreakable()
    {
        setHardness(-1.0F);
        return this;
    }

    public float getHardness() => hardness;

    protected Block setTickRandomly(bool tickRandomly)
    {
        BlocksRandomTick[id] = tickRandomly;
        return this;
    }

    public Block setBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        BoundingBox = new Box(minX, minY, minZ, maxX, maxY, maxZ);
        return this;
    }

    public virtual float getLuminance(ILightProvider lighting, int x, int y, int z)
    {
        float baseLuminance;
        if (lighting != null)
        {
            baseLuminance = lighting.GetNaturalBrightness(x, y, z, BlocksLightLuminance[id]);
        }
        else
        {
            int baseLum = BlocksLightLuminance[id];
            baseLuminance = baseLum > 0 ? baseLum / 15.0f : 1.0f;
        }

        return Visuals == null ? baseLuminance : Visuals.GetLuminance(this, lighting!, x, y, z, baseLuminance);
    }

    public virtual bool isSideVisible(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        double minX = BoundingBox.MinX;
        double minY = BoundingBox.MinY;
        double minZ = BoundingBox.MinZ;
        double maxX = BoundingBox.MaxX;
        double maxY = BoundingBox.MaxY;
        double maxZ = BoundingBox.MaxZ;

        bool baseVisibility = !side.IsValidSide()
            ? !iBlockReader.IsOpaque(x, y, z)
            : side switch
            {
                Side.Down => minY > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.Up => maxY < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.North => minZ > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.South => maxZ < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.West => minX > 0.0D || !iBlockReader.IsOpaque(x, y, z),
                Side.East => maxX < 1.0D || !iBlockReader.IsOpaque(x, y, z),
                _ => !iBlockReader.IsOpaque(x, y, z)
            };
        return Visuals == null ? baseVisibility : Visuals.IsSideVisible(this, iBlockReader, x, y, z, side, baseVisibility);
    }

    public virtual bool isSolidFace(IBlockReader iBlockReader, int x, int y, int z, int face) => iBlockReader.GetMaterial(x, y, z).IsSolid;

    public virtual int GetTextureId(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        int baseTexture = GetTexture(side, iBlockReader.GetBlockMeta(x, y, z));
        return Visuals == null ? baseTexture : Visuals.GetTextureId(this, iBlockReader, x, y, z, side, baseTexture);
    }

    public virtual int GetTexture(Side side, int meta)
    {
        int baseTexture = GetTexture(side);
        return Visuals == null ? baseTexture : Visuals.GetTexture(this, side, meta, baseTexture);
    }

    public virtual int GetTexture(Side side)
    {
        int baseTexture = _faceTextureIds?[(int)side] ?? TextureId;
        return Visuals == null ? baseTexture : Visuals.GetTexture(this, side, baseTexture);
    }

    public virtual Box getBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        updateBoundingBox(world, entities, x, y, z);
        return BoundingBox.Offset(x, y, z);
    }

    public virtual void addIntersectingBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z, Box box, List<Box> boxes)
    {
        if (Physics != null)
        {
            int countBefore = boxes.Count;
            Physics.AddCollisionBoxes(this, world, x, y, z, box, boxes);
            if (boxes.Count > countBefore) return;
        }

        Box? collisionBox = getCollisionShape(world, entities, x, y, z);
        if (collisionBox != null && box.Intersects(collisionBox.Value))
        {
            boxes.Add(collisionBox.Value);
        }
    }

    public virtual Box? getCollisionShape(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        updateBoundingBox(world, entities, x, y, z);
        Box? defaultShape = _hasCollision ? BoundingBox.Offset(x, y, z) : null;
        return Physics == null ? defaultShape : Physics.GetCollisionShape(this, world, entities, x, y, z, defaultShape);
    }

    public virtual bool isOpaque() => Visuals == null ? _isOpaque : Visuals.IsOpaque(this, _isOpaque);

    public virtual bool hasCollision(int meta, bool allowLiquids) => Physics == null ? hasCollision() : Physics.HasCollision(this, meta, allowLiquids, hasCollision());

    public virtual bool hasCollision() => Physics == null || Physics.HasCollision(this, true);

    public IBlockTicker? Ticker { get; private set; }

    public Block SetTicker(IBlockTicker ticker)
    {
        Ticker = ticker;
        return this;
    }

    public virtual void onTick(OnTickEvent e) => Ticker?.OnTick(this, e);

    public virtual void randomDisplayTick(OnTickEvent e) => Ticker?.RandomDisplayTick(this, e);

    public virtual void onMetadataChange(OnMetadataChangeEvent ctx) => Lifecycle?.OnMetadataChange(this, ctx);

    public virtual void neighborUpdate(OnTickEvent e) => Physics?.NeighborUpdate(this, e);

    public virtual int getTickRate() => _tickRate;

    public virtual void onPlaced(OnPlacedEvent e) => Lifecycle?.OnPlaced(this, e);

    protected Block setTickRate(int rate)
    {
        _tickRate = rate;
        return this;
    }

    public virtual void onBreak(OnBreakEvent e) => Lifecycle?.OnBreak(this, e);

    public virtual int getDroppedItemCount()
    {
        int defaultCount = _minDroppedCount == _maxDroppedCount ? _minDroppedCount : _minDroppedCount + Random.Shared.Next(_maxDroppedCount - _minDroppedCount + 1);
        return Lifecycle == null ? defaultCount : Lifecycle.GetDroppedItemCount(this, defaultCount);
    }

    public virtual int getDroppedItemId(int blockMeta)
    {
        int defaultId = _lootTable == null ? id : _lootTable.Roll(Random.Shared);
        return Lifecycle == null ? defaultId : Lifecycle.GetDroppedItemId(this, blockMeta, defaultId);
    }

    public float getHardness(EntityPlayer player) => hardness < 0.0F ? 0.0F : !player.CanHarvest(this) ? 1.0F / hardness / 100.0F : player.GetBlockBreakingSpeed(this) / hardness / 30.0F;

    public virtual void dropStacks(OnDropEvent ctx)
    {
        if (!ctx.World.IsRemote && ctx.World.Rules.GetBool(DefaultRules.DoTileDrops))
        {
            int dropCount = getDroppedItemCount();

            for (int attempt = 0; attempt < dropCount; ++attempt)
            {
                if (Random.Shared.NextSingle() <= ctx.Luck)
                {
                    int itemId = getDroppedItemId(ctx.Meta);
                    if (itemId > 0)
                    {
                        dropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(itemId, 1, getDroppedItemMeta(ctx.Meta)));
                    }
                }
            }
        }

        Lifecycle?.OnDropStacks(this, ctx);
    }

    public static void dropStack(IWorldContext world, int x, int y, int z, ItemStack itemStack)
    {
        if (!world.IsRemote && world.Rules.GetBool(DefaultRules.DoTileDrops))
        {
            float spreadFactor = 0.7F;
            double offsetX = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
            double offsetY = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
            double offsetZ = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
            world.SpawnItemDrop(x + offsetX, y + offsetY, z + offsetZ, itemStack);
        }
    }

    protected virtual int getDroppedItemMeta(int blockMeta)
    {
        int defaultMeta = _dropsWithBlockMeta ? blockMeta : _droppedItemMetaValue;
        return Lifecycle == null ? defaultMeta : Lifecycle.GetDroppedItemMeta(this, blockMeta, defaultMeta);
    }

    public virtual float getBlastResistance(Entity entity) => resistance / 5.0F;

    public virtual HitResult raycast(IBlockReader world, EntityManager entities, int x, int y, int z, Vec3D startPos, Vec3D endPos)
    {
        updateBoundingBox(world, entities, x, y, z);
        Vec3D pos = new(x, y, z);
        HitResult res = BoundingBox.Raycast(startPos - pos, endPos - pos);
        if (res.Type == HitResultType.MISS)
        {
            return new HitResult(HitResultType.MISS);
        }

        res.BlockX = x;
        res.BlockY = y;
        res.BlockZ = z;
        res.Pos += pos;
        return res;
    }

    public virtual void onDestroyedByExplosion(OnDestroyedByExplosionEvent @event) => Lifecycle?.OnDestroyedByExplosion(this, @event);

    public virtual int getRenderLayer() => _renderLayer;

    protected Block setRenderLayer(int layer)
    {
        _renderLayer = layer;
        return this;
    }

    protected Block setSlipperiness(float slipperiness)
    {
        Slipperiness = slipperiness;
        return this;
    }

    public virtual bool canPlaceAt(CanPlaceAtContext evt)
    {
        int blockId = evt.World.Reader.GetBlockId(evt.X, evt.Y, evt.Z);
        bool baseResult = blockId == 0 || Blocks[blockId].material.IsReplaceable;
        return Physics == null ? baseResult : baseResult && Physics.CanPlaceAt(this, evt);
    }

    public IBlockInteractable? Interactable { get; private set; }

    public Block SetInteractable(IBlockInteractable interactable)
    {
        Interactable = interactable;
        return this;
    }

    public IBlockVisuals? Visuals { get; private set; }

    public Block SetVisuals(IBlockVisuals visuals)
    {
        Visuals = visuals;
        return this;
    }

    public IBlockLifecycle? Lifecycle { get; private set; }

    public Block SetLifecycle(IBlockLifecycle lifecycle)
    {
        Lifecycle = lifecycle;
        return this;
    }

    public IBlockPhysics? Physics { get; private set; }

    public Block SetPhysics(IBlockPhysics physics)
    {
        Physics = physics;
        return this;
    }

    public IRedstoneComponent? Redstone { get; private set; }

    public Block SetRedstone(IRedstoneComponent redstone)
    {
        Redstone = redstone;
        return this;
    }

    public virtual bool onUse(OnUseEvent ctx) => Interactable?.OnUse(this, ctx) ?? false;

    public virtual void onSteppedOn(OnEntityStepEvent @event) => Interactable?.OnSteppedOn(this, @event);

    public virtual void onBlockBreakStart(OnBlockBreakStartEvent @event) => Interactable?.OnBlockBreakStart(this, @event);

    public virtual Vec3D applyVelocity(OnApplyVelocityEvent @event) => Physics == null ? Vec3D.Zero : Physics.ApplyVelocity(this, @event, Vec3D.Zero);

    public void updateBoundingBox(IBlockReader blockReader, int x, int y, int z) => updateBoundingBox(blockReader, null, x, y, z);

    public virtual void updateBoundingBox(IBlockReader blockReader, EntityManager? entities, int x, int y, int z) => Physics?.UpdateBoundingBox(this, blockReader, entities, x, y, z);

    public virtual int getColor(int meta) => Visuals == null ? 0xFFFFFF : Visuals.GetColor(this, meta, 0xFFFFFF);

    public virtual int getColorForFace(int meta, int face)
    {
        int baseColor = getColor(meta);
        return Visuals == null ? baseColor : Visuals.GetColorForFace(this, meta, face, baseColor);
    }

    public virtual int getColorMultiplier(IBlockReader iBlockReader, int x, int y, int z) => Visuals == null ? 0xFFFFFF : Visuals.GetColorMultiplier(this, iBlockReader, x, y, z, 0xFFFFFF);

    public virtual int getColorMultiplier(IBlockReader iBlockReader, int x, int y, int z, int knownMeta)
    {
        int baseColor = getColorMultiplier(iBlockReader, x, y, z);
        return Visuals == null ? baseColor : Visuals.GetColorMultiplier(this, iBlockReader, x, y, z, knownMeta, baseColor);
    }

    public virtual bool isPoweringSide(IBlockReader iBlockReader, int x, int y, int z, int side) => Redstone != null && Redstone.IsPoweringSide(this, iBlockReader, x, y, z, side);

    public virtual bool canEmitRedstonePower() => Redstone != null && Redstone.CanEmitRedstonePower(this);

    public virtual bool isFlammable(IBlockReader iBlockReader, int x, int y, int z) => Physics != null && Physics.IsFlammable(this, iBlockReader, x, y, z, false);

    public virtual void onEntityCollision(OnEntityCollisionEvent @event) => Interactable?.OnEntityCollision(this, @event);

    public virtual bool isStrongPoweringSide(IBlockReader world, int x, int y, int z, int side) => Redstone != null && Redstone.IsStrongPoweringSide(this, world, x, y, z, side);

    public virtual void setupRenderBoundingBox() => Physics?.SetupRenderBoundingBox(this);

    public virtual void onAfterBreak(OnAfterBreakEvent ctx)
    {
        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[id], 1);
        dropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, ctx.Meta));
        Lifecycle?.OnAfterBreak(this, ctx);
    }

    public virtual bool canGrow(OnTickEvent ctx) => Physics == null || Physics.CanGrow(this, ctx);

    public Block setBlockName(string name)
    {
        _blockName = "tile." + name;
        return this;
    }

    public string translateBlockName() => Translations.Get($"{getBlockName()}.name");

    public string getBlockName() => _blockName;

    /// <summary>
    /// Block name without the "tile." prefix, suitable for registry / ResourceLocation lookup
    /// (e.g. "stone", "reeds", "cake").
    /// </summary>
    public string RegistryName => _blockName.StartsWith("tile.") ? _blockName[5..] : _blockName;

    private static Dictionary<string, int>? s_registryNameToId;

    /// <summary>
    /// Looks up a block by its namespaced name (e.g. "betasharp:reeds" or "reeds").
    /// Returns null when the namespace is not <see cref="Namespace.BetaSharp"/> or the name is unknown.
    /// </summary>
    public static Block? ByName(string raw)
    {
        var location = ResourceLocation.Parse(raw);
        if (location.Namespace != Namespace.BetaSharp) return null;

        if (s_registryNameToId is null)
        {
            s_registryNameToId = [];
            foreach (Block? b in Blocks)
            {
                if (b is not null) s_registryNameToId.TryAdd(b.RegistryName, b.id);
            }
        }

        return s_registryNameToId.TryGetValue(location.Path, out int id) ? Blocks[id] : null;
    }

    public virtual IReadOnlyList<string> GetBlockAlias => _blockAlias ?? [];

    public virtual void onBlockAction(OnBlockActionEvent ctx) => Lifecycle?.OnBlockAction(this, ctx);

    public bool getEnableStats() => shouldTrackStatistics;

    protected Block disableStats()
    {
        shouldTrackStatistics = false;
        return this;
    }

    public virtual PistonBehavior getPistonBehavior() => _pistonBehaviorOverride ?? material.PistonBehavior;

    /// <summary>Overrides the material-derived piston behavior (e.g. plates are destroyed when pushed).</summary>
    protected Block setPistonBehavior(PistonBehavior behavior)
    {
        _pistonBehaviorOverride = behavior;
        return this;
    }

    /// <summary>
    /// Declares that this block carries a tile entity, created by <paramref name="factory"/>.
    /// The factory is deferred, so it may safely reference types regardless of declaration order.
    /// </summary>
    protected Block setHasTileEntity(Func<BlockEntity> factory)
    {
        BlocksWithEntity[id] = true;
        _blockEntityFactory = factory;
        return this;
    }

    public virtual BlockEntity? getBlockEntity() => _blockEntityFactory?.Invoke();
}
