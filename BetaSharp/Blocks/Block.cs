using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Rules;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks;

public class Block
{
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
    private static readonly PlantSurvivalBehavior s_deadBushSurvival = new(id => id == Sand.Id);
    private static readonly LogBehavior s_logBehavior = new();
    private static readonly MeltBehavior s_iceMelt = new(() => Water.Id, true, () => FlowingWater.Id);
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
    private static readonly LockedChestBehavior s_lockedChestBehavior = new();

    public static readonly Block Stone = new Block(1, BlockTextures.Stone, Material.Stone)
        .SetLootTable(new LootTable(new LootEntry(() => Cobblestone.Id)))
        .SetHardness(1.5F)
        .SetResistance(10.0F)
        .setSoundGroup(SoundStoneFootstep)
        .SetBlockName("stone");

    public static readonly Block GrassBlock = new Block(2, BlockTextures.GrassSide, Material.SolidOrganic)
        .SetTopBottomTextures(BlockTextures.GrassTop, BlockTextures.Dirt)
        .SetVisuals(new GrassVisualBehavior())
        .SetTicker(new GrassTickerBehavior())
        .SetTickRandomly(true)
        .SetLootTable(new LootTable(new LootEntry(() => Dirt.Id)))
        .SetHardness(0.6F)
        .setSoundGroup(SoundGrassFootstep)
        .SetBlockName("grass")
        .SetVariance(TextureVariance.Rotations, TextureVariance.All, TextureVariance.None);

    public static readonly Block Dirt = new Block(3, BlockTextures.Dirt, Material.Soil).SetHardness(0.5F).setSoundGroup(SoundGravelFootstep).SetBlockName("dirt").SetVariance(TextureVariance.All);
    public static readonly Block Cobblestone = new Block(4, BlockTextures.Cobblestone, Material.Stone).SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("stonebrick");
    public static readonly Block Planks = new Block(5, BlockTextures.OakPlanks, Material.Wood).SetHardness(2.0F).SetResistance(5.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("wood").IgnoreMetaUpdates();

    public static readonly Block Sapling = new Block(6, BlockTextures.SaplingOak, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.5F - 0.4F, 0.0F, 0.5F - 0.4F, 0.5F + 0.4F, 0.8F, 0.5F + 0.4F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_saplingBehavior).SetPhysics(s_plantSurvival).SetVisuals(s_saplingBehavior).SetLifecycle(s_saplingBehavior)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("sapling").IgnoreMetaUpdates();

    public static readonly Block Bedrock = new Block(7, BlockTextures.Bedrock, Material.Stone).SetUnbreakable().SetResistance(6000000.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("bedrock").DisableStats()
        .SetVariance(TextureVariance.All);

    public static readonly Block FlowingWater = new Block(8, (Material.Water == Material.Lava ? 14 : 12) * 16 + 13, Material.Water)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).SetTickRandomly(true).SetTickRate(5)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_flowingFluidBehavior).SetVisuals(s_flowingFluidBehavior).SetLifecycle(s_flowingFluidBehavior).SetTicker(s_flowingFluidBehavior)
        .SetDropCount(0).SetRenderLayer(1)
        .SetHardness(100.0F).setOpacity(3).SetBlockName("water").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Water = new Block(9, (Material.Water == Material.Lava ? 14 : 12) * 16 + 13, Material.Water)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).SetTickRandomly(false).SetTickRate(5)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_stationaryFluidBehavior).SetVisuals(s_stationaryFluidBehavior).SetLifecycle(s_stationaryFluidBehavior).SetTicker(s_stationaryFluidBehavior)
        .SetDropCount(0).SetRenderLayer(1)
        .SetHardness(100.0F).setOpacity(3).SetBlockName("water").DisableStats().IgnoreMetaUpdates();

    public static readonly Block FlowingLava = new Block(10, (Material.Lava == Material.Lava ? 14 : 12) * 16 + 13, Material.Lava)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).SetTickRandomly(true).SetTickRate(30)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_flowingFluidBehavior).SetVisuals(s_flowingFluidBehavior).SetLifecycle(s_flowingFluidBehavior).SetTicker(s_flowingFluidBehavior)
        .SetDropCount(0)
        .SetHardness(0.0F).SetLuminance(1.0F).setOpacity(255).SetBlockName("lava").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Lava = new Block(11, (Material.Lava == Material.Lava ? 14 : 12) * 16 + 13, Material.Lava)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F).SetTickRandomly(true).SetTickRate(30)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Fluids)
        .SetPhysics(s_stationaryFluidBehavior).SetVisuals(s_stationaryFluidBehavior).SetLifecycle(s_stationaryFluidBehavior).SetTicker(s_stationaryFluidBehavior)
        .SetDropCount(0)
        .SetHardness(100.0F).SetLuminance(1.0F).setOpacity(255).SetBlockName("lava").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Sand = new Block(12, BlockTextures.Sand, Material.Sand)
        .SetTicker(s_fallingBehavior).SetLifecycle(s_fallingBehavior).SetPhysics(s_fallingBehavior).SetTickRate(3)
        .SetHardness(0.5F).setSoundGroup(SoundSandFootstep).SetBlockName("sand").SetVariance(TextureVariance.Rotations);

    public static readonly Block Gravel = new Block(13, BlockTextures.Gravel, Material.Sand)
        .SetTicker(s_fallingBehavior).SetLifecycle(s_fallingBehavior).SetPhysics(s_fallingBehavior).SetTickRate(3)
        .SetLootTable(new LootTable(new LootEntry(() => Gravel.Id, 9), new LootEntry(() => Item.ByName("flint").Id, 1)))
        .SetHardness(0.6F).setSoundGroup(SoundGravelFootstep).SetBlockName("gravel").SetVariance(TextureVariance.Rotations);

    public static readonly Block GoldOre = new Block(14, BlockTextures.GoldOre, Material.Stone).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("oreGold")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block IronOre = new Block(15, BlockTextures.IronOre, Material.Stone).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("oreIron")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block CoalOre = new Block(16, BlockTextures.CoalOre, Material.Stone).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("coal").Id))).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep)
        .SetBlockName("oreCoal")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block Log = new Block(17, BlockTextures.LogOakSide, Material.Wood)
        .SetVisuals(s_logBehavior).SetLifecycle(s_logBehavior).preserveMetaOnDrop()
        .SetHardness(2.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("log").IgnoreMetaUpdates().SetVariance(TextureVariance.All, TextureVariance.Rotate180);

    // isOpaque() is cached into BlocksOpaque[id] before SetVisuals runs, so it snapshots the base
    // default (true) here — matching original BlockLeavesBase(..., graphicsLevel: false). The live
    // isOpaque() call (used for rendering) toggles dynamically via LeavesBehavior once Visuals is set.
    public static readonly Block Leaves = new Block(18, BlockTextures.LeavesOak, Material.Leaves)
        .SetTicker(s_leavesBehavior).SetLifecycle(s_leavesBehavior).SetVisuals(s_leavesBehavior)
        .SetHardness(0.2F).setOpacity(1).setSoundGroup(SoundGrassFootstep).SetBlockName("leaves").DisableStats().IgnoreMetaUpdates()
        .SetVariance(TextureVariance.All, TextureVariance.Rotate180);

    public static readonly Block Sponge = new Block(19, BlockTextures.Sponge, Material.Sponge)
        .SetLifecycle(new SpongeLifecycleBehavior())
        .SetHardness(0.6F).setSoundGroup(SoundGrassFootstep).SetBlockName("sponge").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block Glass = new Block(20, BlockTextures.Glass, Material.Glass).SetNonOpaque().SetDropCount(0).SetVisuals(new GlassVisualBehavior(false))
        .SetHardness(0.3F).setSoundGroup(SoundGlassFootstep).SetBlockName("glass").SetVariance(TextureVariance.Rotate180);

    public static readonly Block LapisOre = new Block(21, BlockTextures.LapisOre, Material.Stone).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("dye_powder").Id)), 4, 8, 4).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep)
        .SetBlockName("oreLapis")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block LapisBlock = new Block(22, BlockTextures.BlockLapis, Material.Stone).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("blockLapis");

    public static readonly Block Dispenser = new Block(23, BlockTextures.FurnaceSide, Material.Stone)
        .SetHasTileEntity(() => new BlockEntityDispenser())
        .SetInteractable(s_dispenserBehavior).SetLifecycle(s_dispenserBehavior).SetPhysics(s_dispenserBehavior).SetTicker(s_dispenserBehavior).SetVisuals(s_dispenserBehavior)
        .SetTickRate(4)
        .SetHardness(3.5F).setSoundGroup(SoundStoneFootstep).SetBlockName("dispenser").IgnoreMetaUpdates();

    public static readonly Block Sandstone = new Block(24, BlockTextures.SandstoneSide, Material.Stone).SetTopBottomTextures(BlockTextures.SandstoneTop, BlockTextures.SandstoneBottom).setSoundGroup(SoundStoneFootstep).SetHardness(0.8F)
        .SetBlockName("sandStone").SetVariance(TextureVariance.Rotations, TextureVariance.None);

    public static readonly Block Noteblock = new Block(25, BlockTextures.NoteBlock, Material.Wood)
        .SetHasTileEntity(() => new BlockEntityNote())
        .SetInteractable(s_noteblockBehavior).SetLifecycle(s_noteblockBehavior).SetPhysics(s_noteblockBehavior)
        .SetHardness(0.8F).SetBlockName("musicBlock").IgnoreMetaUpdates();

    public static readonly Block Bed = new Block(26, BlockTextures.BedTopFoot, Material.Wool)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 9.0F / 16.0F, 1.0F)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Bed).SetPistonBehavior(PistonBehavior.Destroy)
        .SetInteractable(s_bedBehavior).SetPhysics(s_bedBehavior).SetLifecycle(s_bedBehavior).SetVisuals(s_bedBehavior)
        .SetHardness(0.2F).SetBlockName("bed").DisableStats().IgnoreMetaUpdates();

    public static readonly Block PoweredRail = new Block(27, BlockTextures.PoweredRailOn, Material.PistonBreakable)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.MinecartTrack).SetNoCollision()
        .SetPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_poweredRail).SetLifecycle(s_poweredRail).SetVisuals(s_poweredRail)
        .SetHardness(0.7F).setSoundGroup(SoundMetalFootstep).SetBlockName("goldenRail").IgnoreMetaUpdates();

    public static readonly Block DetectorRail = new Block(28, BlockTextures.DetectorRail, Material.PistonBreakable)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.MinecartTrack).SetNoCollision()
        .SetPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_poweredRail).SetLifecycle(s_poweredRail).SetVisuals(s_poweredRail)
        .SetTickRandomly(true).SetTickRate(20)
        .SetRedstone(s_detectorRail).SetTicker(s_detectorRail).SetInteractable(s_detectorRail)
        .SetHardness(0.7F).setSoundGroup(SoundMetalFootstep).SetBlockName("detectorRail").IgnoreMetaUpdates();

    public static readonly Block StickyPiston = new Block(29, BlockTextures.PistonTopSticky, Material.Piston)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.PistonBase)
        .SetPhysics(s_stickyPistonBaseBehavior).SetLifecycle(s_stickyPistonBaseBehavior).SetTicker(s_stickyPistonBaseBehavior).SetVisuals(s_stickyPistonBaseBehavior)
        .setSoundGroup(SoundStoneFootstep).SetHardness(0.5F).SetBlockName("pistonStickyBase").IgnoreMetaUpdates();

    public static readonly Block Cobweb = new Block(30, BlockTextures.Cobweb, Material.Cobweb)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetInteractable(s_webBehavior).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("string").Id)))
        .setOpacity(1).SetHardness(4.0F).SetBlockName("web");

    public static readonly Block Grass = new Block(31, BlockTextures.TallGrass, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.5F - 0.4F, 0.0F, 0.5F - 0.4F, 0.5F + 0.4F, 0.8F, 0.5F + 0.4F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival).SetVisuals(s_tallGrassBehavior).SetLifecycle(s_tallGrassBehavior)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("tallgrass");

    public static readonly Block DeadBush = new Block(32, BlockTextures.DeadBush, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.1F, 0.0F, 0.1F, 0.9F, 0.8F, 0.9F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_deadBushSurvival).SetPhysics(s_deadBushSurvival).SetDropCount(0)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("deadbush");

    public static readonly Block Piston = new Block(33, BlockTextures.PistonTopNormal, Material.Piston)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.PistonBase)
        .SetPhysics(s_pistonBaseBehavior).SetLifecycle(s_pistonBaseBehavior).SetTicker(s_pistonBaseBehavior).SetVisuals(s_pistonBaseBehavior)
        .setSoundGroup(SoundStoneFootstep).SetHardness(0.5F).SetBlockName("pistonBase").IgnoreMetaUpdates();

    public static readonly Block PistonHead = new Block(34, BlockTextures.PistonTopNormal, Material.Piston)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.PistonExtension).SetDropCount(0)
        .SetPhysics(s_pistonExtensionBehavior).SetLifecycle(s_pistonExtensionBehavior).SetVisuals(s_pistonExtensionBehavior)
        .setSoundGroup(SoundStoneFootstep).SetHardness(0.5F).IgnoreMetaUpdates();

    public static readonly Block Wool = new Block(35, 64, Material.Wool).SetVisuals(new ClothVisualBehavior()).preserveMetaOnDrop()
        .SetBlockAlias(
            "blackWool:15", "redWool:14", "greenWool:13", "brownWool:12",
            "blueWool:11", "purpleWool:10", "cyanWool:9", "silverWool:8",
            "grayWool:7", "pinkWool:6", "limeWool:5", "yellowWool:4",
            "lightBlueWool:3", "magentaWool:2", "orangeWool:1", "whiteWool:0")
        .SetHardness(0.8F).setSoundGroup(SoundClothFootstep).SetBlockName("cloth").IgnoreMetaUpdates().SetVariance(TextureVariance.None, TextureVariance.FlipBoth);

    public static readonly Block MovingPiston = new Block(36, Material.Piston)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Entity)
        .SetPhysics(s_pistonMovingBehavior).SetLifecycle(s_pistonMovingBehavior).SetInteractable(s_pistonMovingBehavior)
        .SetHardness(-1.0F);

    public static readonly Block Dandelion = new Block(37, BlockTextures.Dandelion, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.6F, 0.7F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("flower");

    public static readonly Block Rose = new Block(38, BlockTextures.Rose, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.6F, 0.7F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_plantSurvival).SetPhysics(s_plantSurvival)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("rose");

    public static readonly Block BrownMushroom = new Block(39, BlockTextures.BrownMushroom, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.4F, 0.7F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_mushroomBehavior).SetPhysics(s_mushroomBehavior)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetLuminance(2.0F / 16.0F).SetBlockName("mushroom");

    public static readonly Block RedMushroom = new Block(40, BlockTextures.RedMushroom, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.3F, 0.0F, 0.3F, 0.7F, 0.4F, 0.7F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_mushroomBehavior).SetPhysics(s_mushroomBehavior)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("mushroom");

    public static readonly Block GoldBlock = new Block(41, BlockTextures.BlockGold, Material.Metal).SetHardness(3.0F).SetResistance(10.0F).setSoundGroup(SoundMetalFootstep).SetBlockName("blockGold");
    public static readonly Block IronBlock = new Block(42, BlockTextures.BlockIron, Material.Metal).SetHardness(5.0F).SetResistance(10.0F).setSoundGroup(SoundMetalFootstep).SetBlockName("blockIron");

    public static readonly Block DoubleSlab = new Block(43, BlockTextures.StoneSlabTop, Material.Stone)
        .SetPhysics(s_doubleSlab).SetLifecycle(s_doubleSlab).SetVisuals(s_doubleSlab)
        .SetLootTable(new LootTable(new LootEntry(() => Slab.Id))).SetDropCount(2).preserveMetaOnDrop()
        .SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("stoneSlab");

    public static readonly Block Slab = new Block(44, BlockTextures.StoneSlabTop, Material.Stone)
        .SetNonOpaque().SetNotFullCube().setOpacity(255)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F)
        .SetPhysics(s_singleSlab).SetLifecycle(s_singleSlab).SetVisuals(s_singleSlab)
        .SetLootTable(new LootTable(new LootEntry(() => Slab.Id))).preserveMetaOnDrop()
        .SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("stoneSlab");

    public static readonly Block Bricks = new Block(45, BlockTextures.Bricks, Material.Stone).SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("brick");

    public static readonly Block TNT = new Block(46, BlockTextures.TntSide, Material.Tnt)
        .SetPhysics(s_tntBehavior).SetLifecycle(s_tntBehavior).SetInteractable(s_tntBehavior).SetVisuals(s_tntBehavior)
        .SetDropCount(0)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("tnt");

    public static readonly Block Bookshelf = new Block(47, BlockTextures.Bookshelf, Material.Wood).SetTopBottomTextures(BlockTextures.OakPlanks, BlockTextures.OakPlanks).SetDropCount(0).SetHardness(1.5F).setSoundGroup(SoundWoodFootstep)
        .SetBlockName("bookshelf").SetVariance(TextureVariance.None, TextureVariance.FlipU);

    public static readonly Block MossyCobblestone = new Block(48, BlockTextures.MossyCobblestone, Material.Stone).SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("stoneMoss");
    public static readonly Block Obsidian = new Block(49, BlockTextures.Obsidian, Material.Stone).SetHardness(10.0F).SetResistance(2000.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("obsidian");

    public static readonly Block Torch = new Block(50, BlockTextures.Torch, Material.PistonBreakable)
        .SetTickRandomly(true).SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Torch)
        .SetPhysics(s_torchBehavior).SetLifecycle(s_torchBehavior).SetTicker(s_torchBehavior)
        .SetHardness(0.0F).SetLuminance(15.0F / 16.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("torch").IgnoreMetaUpdates();

    public static readonly Block Fire = new Block(51, BlockTextures.Fire, Material.Fire)
        .SetTickRandomly(true).SetTickRate(40).SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Fire)
        .SetTicker(s_fireBehavior).SetPhysics(s_fireBehavior).SetLifecycle(s_fireBehavior)
        .SetDropCount(0)
        .SetHardness(0.0F).SetLuminance(1.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("fire").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Spawner = new Block(52, BlockTextures.Spawner, Material.Stone)
        .SetHasTileEntity(() => new BlockEntityMobSpawner())
        .SetLifecycle(s_tileEntityLifecycle)
        .SetDropCount(0).SetNonOpaque()
        .SetHardness(5.0F).setSoundGroup(SoundMetalFootstep).SetBlockName("mobSpawner").DisableStats();

    public static readonly Block WoodenStairs = new Block(53, Planks.TextureId, Planks.Material)
        .SetHardness(Planks.Hardness).SetResistance(Planks.Resistance / 3.0F).setSoundGroup(Planks.SoundGroup)
        .setOpacity(255).SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Stairs)
        .SetPhysics(s_woodStairs).SetLifecycle(s_woodStairs).SetVisuals(s_woodStairs)
        .SetBlockName("stairsWood").IgnoreMetaUpdates();

    public static readonly Block Chest = new Block(54, BlockTextures.ChestSingleSide, Material.Wood)
        .SetHasTileEntity(() => new BlockEntityChest())
        .SetInteractable(s_chestBehavior).SetLifecycle(s_chestBehavior).SetPhysics(s_chestBehavior).SetVisuals(s_chestBehavior)
        .SetHardness(2.5F).setSoundGroup(SoundWoodFootstep).SetBlockName("chest").IgnoreMetaUpdates();

    public static readonly Block RedstoneWire = new Block(55, BlockTextures.RedstoneWireCross, Material.PistonBreakable)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F / 16.0F, 1.0F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.RedstoneWire)
        .SetRedstone(s_redstoneWire).SetPhysics(s_redstoneWire).SetTicker(s_redstoneWire).SetLifecycle(s_redstoneWire).SetVisuals(s_redstoneWire)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)))
        .SetHardness(0.0F).setSoundGroup(SoundPowderFootstep).SetBlockName("redstoneDust").DisableStats().IgnoreMetaUpdates();

    public static readonly Block DiamondOre = new Block(56, BlockTextures.DiamondOre, Material.Stone).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("diamond").Id))).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep)
        .SetBlockName("oreDiamond")
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block DiamondBlock = new Block(57, BlockTextures.BlockDiamond, Material.Metal).SetHardness(5.0F).SetResistance(10.0F).setSoundGroup(SoundMetalFootstep).SetBlockName("blockDiamond");

    public static readonly Block CraftingTable = new Block(58, BlockTextures.CraftingTableSide, Material.Wood).SetTopBottomTextures(BlockTextures.CraftingTableTop, BlockTextures.OakPlanks)
        .SetFaceTexture(Side.North, BlockTextures.CraftingTableFront).SetFaceTexture(Side.West, BlockTextures.CraftingTableFront)
        .SetHardness(2.5F).setSoundGroup(SoundWoodFootstep).SetBlockName("workbench").SetInteractable(new WorkbenchInteractBehavior());

    public static readonly Block Wheat = new Block(59, BlockTextures.WheatStageBase, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.25F, 1.0F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Crops)
        .SetTicker(s_cropBehavior).SetPhysics(s_cropBehavior).SetLifecycle(s_cropBehavior).SetVisuals(s_cropBehavior)
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("crops").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Farmland = new Block(60, BlockTextures.FarmlandDry, Material.Soil)
        .SetTickRandomly(true).SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 15.0F / 16.0F, 1.0F)
        .SetNonOpaque().SetNotFullCube().setOpacity(255)
        .SetTicker(s_farmlandBehavior).SetPhysics(s_farmlandBehavior).SetInteractable(s_farmlandBehavior).SetLifecycle(s_farmlandBehavior).SetVisuals(s_farmlandBehavior)
        .SetHardness(0.6F).setSoundGroup(SoundGravelFootstep).SetBlockName("farmland");

    public static readonly Block Furnace = new Block(61, BlockTextures.FurnaceSide, Material.Stone)
        .SetHasTileEntity(() => new BlockEntityFurnace())
        .SetInteractable(s_furnaceUnlit).SetLifecycle(s_furnaceUnlit).SetPhysics(s_furnaceUnlit).SetTicker(s_furnaceUnlit).SetVisuals(s_furnaceUnlit)
        .SetHardness(3.5F).setSoundGroup(SoundStoneFootstep).SetBlockName("furnace").IgnoreMetaUpdates();

    public static readonly Block LitFurnace = new Block(62, BlockTextures.FurnaceSide, Material.Stone)
        .SetHasTileEntity(() => new BlockEntityFurnace())
        .SetInteractable(s_furnaceLit).SetLifecycle(s_furnaceLit).SetPhysics(s_furnaceLit).SetTicker(s_furnaceLit).SetVisuals(s_furnaceLit)
        .SetLootTable(new LootTable(new LootEntry(() => Furnace.Id)))
        .SetHardness(3.5F).setSoundGroup(SoundStoneFootstep).SetLuminance(14.0F / 16.0F).SetBlockName("furnace").IgnoreMetaUpdates();

    public static readonly Block Sign = new Block(63, BlockTextures.OakPlanks, Material.Wood)
        .SetHasTileEntity(() => new BlockEntitySign())
        .SetBoundingBox(0.25F, 0.0F, 0.25F, 0.75F, 1.0F, 0.75F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Entity)
        .SetPhysics(s_standingSign).SetLifecycle(s_tileEntityLifecycle)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("sign").Id)))
        .SetHardness(1.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("sign").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Door = new Block(64, BlockTextures.DoorWood, Material.Wood)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Door)
        .SetPistonBehavior(PistonBehavior.Destroy)
        .SetPhysics(s_woodDoor).SetInteractable(s_woodDoor).SetLifecycle(s_woodDoor).SetVisuals(s_woodDoor)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("door_wood").Id)))
        .SetHardness(3.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("doorWood").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Ladder = new Block(65, BlockTextures.Ladder, Material.PistonBreakable)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Ladder)
        .SetPhysics(s_ladderBehavior).SetLifecycle(s_ladderBehavior)
        .SetHardness(0.4F).setSoundGroup(SoundWoodFootstep).SetBlockName("ladder").IgnoreMetaUpdates();

    public static readonly Block Rail = new Block(66, BlockTextures.RailStraight, Material.PistonBreakable)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.MinecartTrack).SetNoCollision()
        .SetPistonBehavior(PistonBehavior.Normal)
        .SetPhysics(s_normalRail).SetLifecycle(s_normalRail).SetVisuals(s_normalRail)
        .SetHardness(0.7F).setSoundGroup(SoundMetalFootstep).SetBlockName("rail").IgnoreMetaUpdates();

    public static readonly Block CobblestoneStairs = new Block(67, Cobblestone.TextureId, Cobblestone.Material)
        .SetHardness(Cobblestone.Hardness).SetResistance(Cobblestone.Resistance / 3.0F).setSoundGroup(Cobblestone.SoundGroup)
        .setOpacity(255).SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Stairs)
        .SetPhysics(s_cobbleStairs).SetLifecycle(s_cobbleStairs).SetVisuals(s_cobbleStairs)
        .SetBlockName("stairsStone").IgnoreMetaUpdates();

    public static readonly Block WallSign = new Block(68, BlockTextures.OakPlanks, Material.Wood)
        .SetHasTileEntity(() => new BlockEntitySign())
        .SetBoundingBox(0.25F, 0.0F, 0.25F, 0.75F, 1.0F, 0.75F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Entity)
        .SetPhysics(s_wallSign).SetLifecycle(s_tileEntityLifecycle)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("sign").Id)))
        .SetHardness(1.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("sign").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Lever = new Block(69, BlockTextures.Lever, Material.PistonBreakable)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Lever)
        .SetRedstone(s_leverBehavior).SetPhysics(s_leverBehavior).SetInteractable(s_leverBehavior).SetLifecycle(s_leverBehavior)
        .SetHardness(0.5F).setSoundGroup(SoundWoodFootstep).SetBlockName("lever").IgnoreMetaUpdates();

    public static readonly Block StonePressurePlate = new Block(70, BlockTextures.Stone, Material.Stone)
        .SetTickRandomly(true).SetTickRate(20)
        .SetBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 15.0F / 16.0F, 1.0F / 32.0F, 15.0F / 16.0F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetPistonBehavior(PistonBehavior.Destroy)
        .SetRedstone(s_mobPlate).SetPhysics(s_mobPlate).SetTicker(s_mobPlate).SetInteractable(s_mobPlate).SetLifecycle(s_mobPlate)
        .SetHardness(0.5F).setSoundGroup(SoundStoneFootstep).SetBlockName("pressurePlate")
        .IgnoreMetaUpdates();

    public static readonly Block IronDoor = new Block(71, BlockTextures.DoorIron, Material.Metal)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Door)
        .SetPistonBehavior(PistonBehavior.Destroy)
        .SetPhysics(s_ironDoor).SetInteractable(s_ironDoor).SetLifecycle(s_ironDoor).SetVisuals(s_ironDoor)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("door_iron").Id)))
        .SetHardness(5.0F).setSoundGroup(SoundMetalFootstep).SetBlockName("doorIron").DisableStats().IgnoreMetaUpdates();

    public static readonly Block WoodenPressurePlate = new Block(72, BlockTextures.OakPlanks, Material.Wood)
        .SetTickRandomly(true).SetTickRate(20)
        .SetBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 15.0F / 16.0F, 1.0F / 32.0F, 15.0F / 16.0F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetPistonBehavior(PistonBehavior.Destroy)
        .SetRedstone(s_everythingPlate).SetPhysics(s_everythingPlate).SetTicker(s_everythingPlate).SetInteractable(s_everythingPlate).SetLifecycle(s_everythingPlate)
        .SetHardness(0.5F).setSoundGroup(SoundWoodFootstep)
        .SetBlockName("pressurePlate")
        .IgnoreMetaUpdates();

    public static readonly Block RedstoneOre = new Block(73, BlockTextures.RedstoneOre, Material.Stone)
        .SetTickRate(30)
        .SetTicker(s_redstoneOreBehavior).SetInteractable(s_redstoneOreBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)), 4, 5)
        .SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("oreRedstone").IgnoreMetaUpdates()
        .SetVariance(TextureVariance.Rotate180, TextureVariance.FlipBoth);

    public static readonly Block LitRedstoneOre = new Block(74, BlockTextures.RedstoneOre, Material.Stone)
        .SetTickRandomly(true).SetTickRate(30)
        .SetTicker(s_redstoneOreBehavior).SetInteractable(s_redstoneOreBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone").Id)), 4, 5)
        .SetLuminance(10.0F / 16.0F).SetHardness(3.0F).SetResistance(5.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("oreRedstone")
        .IgnoreMetaUpdates()
        .SetVariance(TextureVariance.Rotate180, TextureVariance.Rotate180);

    public static readonly Block RedstoneTorch = new Block(75, BlockTextures.RedstoneTorchUnlit, Material.PistonBreakable)
        .SetTickRandomly(true).SetTickRate(2).SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Torch)
        .SetPhysics(s_redstoneTorchBehavior).SetLifecycle(s_redstoneTorchBehavior).SetRedstone(s_redstoneTorchBehavior).SetTicker(s_redstoneTorchBehavior).SetVisuals(s_redstoneTorchBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => LitRedstoneTorch.Id)))
        .SetHardness(0.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("notGate").IgnoreMetaUpdates();

    public static readonly Block LitRedstoneTorch = new Block(76, BlockTextures.RedstoneTorchLit, Material.PistonBreakable)
        .SetTickRandomly(true).SetTickRate(2).SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Torch)
        .SetPhysics(s_redstoneTorchBehavior).SetLifecycle(s_redstoneTorchBehavior).SetRedstone(s_redstoneTorchBehavior).SetTicker(s_redstoneTorchBehavior).SetVisuals(s_redstoneTorchBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => LitRedstoneTorch.Id)))
        .SetHardness(0.0F).SetLuminance(0.5F).setSoundGroup(SoundWoodFootstep).SetBlockName("notGate").IgnoreMetaUpdates();

    public static readonly Block Button = new Block(77, BlockTextures.Stone, Material.PistonBreakable)
        .SetTickRandomly(true).SetTickRate(20)
        .SetNonOpaque().SetNotFullCube().SetNoCollision()
        .SetRedstone(s_buttonBehavior).SetPhysics(s_buttonBehavior).SetTicker(s_buttonBehavior).SetInteractable(s_buttonBehavior).SetLifecycle(s_buttonBehavior)
        .SetHardness(0.5F).setSoundGroup(SoundStoneFootstep).SetBlockName("button").IgnoreMetaUpdates();

    public static readonly Block Snow = new Block(78, BlockTextures.Snow, Material.SnowLayer)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F).SetTickRandomly(true)
        .SetNonOpaque().SetNotFullCube()
        .SetPhysics(s_snowBehavior).SetTicker(s_snowBehavior).SetLifecycle(s_snowBehavior).SetVisuals(s_snowBehavior)
        .SetHardness(0.1F).setSoundGroup(SoundClothFootstep).SetBlockName("snow").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block Ice = new Block(79, BlockTextures.Ice, Material.Ice)
        .SetTickRandomly(true).SetNonOpaque().SetRenderLayer(1).SetSlipperiness(0.98F).SetDropCount(0)
        .SetVisuals(new GlassVisualBehavior(false)).SetTicker(s_iceMelt).SetLifecycle(s_iceMelt)
        .SetHardness(0.5F).setOpacity(3).setSoundGroup(SoundGlassFootstep).SetBlockName("ice").SetVariance(TextureVariance.Rotate180);

    public static readonly Block SnowBlock = new Block(80, BlockTextures.Snow, Material.SnowBlock)
        .SetTickRandomly(true).SetTicker(s_snowMelt).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("snowball").Id)), 4)
        .SetHardness(0.2F).setSoundGroup(SoundClothFootstep).SetBlockName("snow").SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block Cactus = new Block(81, BlockTextures.CactusSide, Material.Cactus)
        .SetTickRandomly(true).SetBoundingBox(1.0F / 16.0F, 0.0F, 1.0F / 16.0F, 1.0F - 1.0F / 16.0F, 1.0F, 1.0F - 1.0F / 16.0F)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Cactus)
        .SetTicker(s_cactusBehavior).SetPhysics(s_cactusBehavior).SetInteractable(s_cactusBehavior).SetVisuals(s_cactusBehavior)
        .SetHardness(0.4F).setSoundGroup(SoundClothFootstep).SetBlockName("cactus")
        .SetVariance(TextureVariance.All, TextureVariance.All, TextureVariance.Rotate180);

    public static readonly Block Clay = new Block(82, BlockTextures.Clay, Material.Clay).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("clay").Id)), 4).SetHardness(0.6F).setSoundGroup(SoundGravelFootstep).SetBlockName("clay")
        .SetVariance(TextureVariance.Rotations);

    public static readonly Block SugarCane = new Block(83, BlockTextures.SugarCane, Material.Plant)
        .SetTickRandomly(true).SetBoundingBox(0.5F - 6.0F / 16.0F, 0.0F, 0.5F - 6.0F / 16.0F, 0.5F + 6.0F / 16.0F, 1.0F, 0.5F + 6.0F / 16.0F)
        .SetNonOpaque().SetNotFullCube().SetNoCollision().SetRenderType(BlockRendererType.Reed)
        .SetTicker(s_reedBehavior).SetPhysics(s_reedBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("sugar_canes").Id)))
        .SetHardness(0.0F).setSoundGroup(SoundGrassFootstep).SetBlockName("reeds").DisableStats();

    public static readonly Block Jukebox = new Block(84, BlockTextures.NoteBlock, Material.Wood)
        .SetHasTileEntity(() => new BlockEntityRecordPlayer())
        .SetFaceTexture(Side.Up, BlockTextures.JukeboxTop)
        .SetInteractable(s_jukeboxBehavior).SetLifecycle(s_jukeboxBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Jukebox.Id)))
        .SetHardness(2.0F).SetResistance(10.0F).setSoundGroup(SoundStoneFootstep).SetBlockName("jukebox").IgnoreMetaUpdates();

    public static readonly Block Fence = new Block(85, BlockTextures.OakPlanks, Material.Wood)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Fence)
        .SetPhysics(s_fenceBehavior)
        .SetHardness(2.0F).SetResistance(5.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("fence").IgnoreMetaUpdates();

    public static readonly Block Pumpkin = new Block(86, BlockTextures.PumpkinBase, Material.Pumpkin)
        .SetTickRandomly(true)
        .SetPhysics(s_pumpkinBehavior).SetLifecycle(s_pumpkinBehavior).SetVisuals(s_pumpkinBehavior)
        .SetHardness(1.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("pumpkin").IgnoreMetaUpdates();

    public static readonly Block Netherrack = new Block(87, BlockTextures.Netherrack, Material.Stone).SetHardness(0.4F).setSoundGroup(SoundStoneFootstep).SetBlockName("hellrock").SetVariance(TextureVariance.All);

    public static readonly Block Soulsand = new Block(88, BlockTextures.SoulSand, Material.Sand)
        .SetPhysics(s_soulSandBehavior).SetInteractable(s_soulSandBehavior)
        .SetHardness(0.5F).setSoundGroup(SoundSandFootstep).SetBlockName("hellsand");

    public static readonly Block Glowstone = new Block(89, BlockTextures.Glowstone, Material.Stone).SetLootTable(new LootTable(new LootEntry(() => Item.ByName("yellow_dust").Id)), 2, 4).SetHardness(0.3F).setSoundGroup(SoundGlassFootstep)
        .SetLuminance(1.0F).SetBlockName("lightgem")
        .SetVariance(TextureVariance.All, TextureVariance.FlipBoth);

    public static readonly Block NetherPortal = new Block(90, BlockTextures.Portal, Material.NetherPortal)
        .SetNonOpaque().SetNotFullCube().SetRenderLayer(1)
        .SetPhysics(s_portalBehavior).SetVisuals(s_portalBehavior).SetInteractable(s_portalBehavior).SetTicker(s_portalBehavior)
        .SetDropCount(0)
        .SetHardness(-1.0F).setSoundGroup(SoundGlassFootstep).SetLuminance(12.0F / 16.0F).SetBlockName("portal");

    public static readonly Block JackLantern = new Block(91, BlockTextures.PumpkinBase, Material.Pumpkin)
        .SetTickRandomly(true)
        .SetPhysics(s_jackLanternBehavior).SetLifecycle(s_jackLanternBehavior).SetVisuals(s_jackLanternBehavior)
        .SetHardness(1.0F).setSoundGroup(SoundWoodFootstep).SetLuminance(1.0F).SetBlockName("litpumpkin").IgnoreMetaUpdates()
        .SetVariance(TextureVariance.All, TextureVariance.None);

    public static readonly Block Cake = new Block(92, BlockTextures.Cake, Material.Cake)
        .SetTickRandomly(true).SetNonOpaque().SetNotFullCube()
        .SetPhysics(s_cakeBehavior).SetVisuals(s_cakeBehavior).SetInteractable(s_cakeBehavior)
        .SetDropCount(0)
        .SetHardness(0.5F).setSoundGroup(SoundClothFootstep).SetBlockName("cake").DisableStats().IgnoreMetaUpdates();

    public static readonly Block Repeater = new Block(93, 6, Material.PistonBreakable)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Repeater)
        .SetRedstone(s_repeaterBehavior).SetTicker(s_repeaterBehavior).SetPhysics(s_repeaterBehavior).SetInteractable(s_repeaterBehavior).SetLifecycle(s_repeaterBehavior).SetVisuals(s_repeaterBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone_repeater").Id)))
        .SetHardness(0.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("diode").DisableStats().IgnoreMetaUpdates();

    public static readonly Block PoweredRepeater = new Block(94, 6, Material.PistonBreakable)
        .SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F)
        .SetNonOpaque().SetNotFullCube().SetRenderType(BlockRendererType.Repeater)
        .SetRedstone(s_repeaterBehavior).SetTicker(s_repeaterBehavior).SetPhysics(s_repeaterBehavior).SetInteractable(s_repeaterBehavior).SetLifecycle(s_repeaterBehavior).SetVisuals(s_repeaterBehavior)
        .SetLootTable(new LootTable(new LootEntry(() => Item.ByName("redstone_repeater").Id)))
        .SetHardness(0.0F).SetLuminance(10.0F / 16.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("diode").DisableStats().IgnoreMetaUpdates();

    public static readonly Block LockedChest = new Block(95, BlockTextures.ChestSingleSide, Material.Wood)
        .SetTicker(s_lockedChestBehavior).SetVisuals(s_lockedChestBehavior)
        .SetBlockName("lockedchest");

    public static readonly Block Trapdoor = new Block(96, BlockTextures.TrapdoorWood, Material.Wood)
        .SetNonOpaque().SetNotFullCube()
        .SetPhysics(s_trapDoor).SetInteractable(s_trapDoor).SetLifecycle(s_trapDoor)
        .SetHardness(3.0F).setSoundGroup(SoundWoodFootstep).SetBlockName("trapdoor").DisableStats().IgnoreMetaUpdates();

    public readonly int Id;
    public readonly Material Material;
    private string[]? _blockAlias;
    private Func<BlockEntity>? _blockEntityFactory;
    private string _blockName = "";
    private int _droppedItemMetaValue;
    private bool _dropsWithBlockMeta;
    private int?[]? _faceTextureIds;
    private bool _hasCollision = true;
    private bool _isFullCube = true;
    private bool _isOpaque = true;
    private LootTable? _lootTable;
    private int _maxDroppedCount = 1;
    private int _minDroppedCount = 1;
    private PistonBehavior? _pistonBehaviorOverride;
    private int _renderLayer;
    private BlockRendererType _renderType = BlockRendererType.Standard;
    private int _tickRate = 10;
    public Box BoundingBox;
    public float Hardness;
    public float ParticleFallSpeedModifier;
    public float Resistance;
    protected bool ShouldTrackStatistics;
    public float Slipperiness;
    public BlockSoundGroup SoundGroup;
    public int TextureId;

    static Block()
    {
        Item.ITEMS[Wool.Id] = new ItemCloth(Wool.Id - 256).setItemName("cloth");
        Item.ITEMS[Log.Id] = new ItemLog(Log.Id - 256).setItemName("log");
        Item.ITEMS[Slab.Id] = new ItemSlab(Slab.Id - 256).setItemName("stoneSlab");
        Item.ITEMS[Sapling.Id] = new ItemSapling(Sapling.Id - 256).setItemName("sapling");
        Item.ITEMS[Leaves.Id] = new ItemLeaves(Leaves.Id - 256).setItemName("leaves");
        Item.ITEMS[Piston.Id] = new ItemPiston(Piston.Id - 256);
        Item.ITEMS[StickyPiston.Id] = new ItemPiston(StickyPiston.Id - 256);

        for (int blockId = 0; blockId < 256; ++blockId)
        {
            if (Blocks[blockId] == null || Item.ITEMS[blockId] != null) continue;
            Item.ITEMS[blockId] = new ItemBlock(blockId - 256);
            Blocks[blockId].Init();
        }

        BlocksAllowVision[0] = true;
    }

    protected Block(int id, Material material)
    {
        ShouldTrackStatistics = true;
        SoundGroup = SoundPowderFootstep;
        ParticleFallSpeedModifier = 1.0F;
        Slipperiness = 0.6F;
        if (Blocks[id] != null)
        {
            throw new ArgumentException($"Slot {id} is already occupied by {Blocks[id]} when adding {this}", nameof(id));
        }

        this.Material = material;
        Blocks[id] = this;
        this.Id = id;
        SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        BlocksOpaque[id] = IsOpaque();
        BlockLightOpacity[id] = IsOpaque() ? 255 : 0;
        BlocksAllowVision[id] = !material.BlocksVision;
        BlocksWithEntity[id] = false;
    }

    protected Block(int id, int textureId, Material material) : this(id, material) => TextureId = textureId;

    public Block(float particleFallSpeedModifier)
    {
        this.ParticleFallSpeedModifier = particleFallSpeedModifier;
    }

    public static BlockSoundGroup SoundPowderFootstep => SoundGroupRegistry.Get("powder");
    public static BlockSoundGroup SoundWoodFootstep => SoundGroupRegistry.Get("wood");
    public static BlockSoundGroup SoundGravelFootstep => SoundGroupRegistry.Get("gravel");
    public static BlockSoundGroup SoundGrassFootstep => SoundGroupRegistry.Get("grass");
    public static BlockSoundGroup SoundStoneFootstep => SoundGroupRegistry.Get("stone");
    public static BlockSoundGroup SoundMetalFootstep => SoundGroupRegistry.Get("metal");
    public static BlockSoundGroup SoundGlassFootstep => SoundGroupRegistry.Get("glass");
    public static BlockSoundGroup SoundClothFootstep => SoundGroupRegistry.Get("cloth");
    public static BlockSoundGroup SoundSandFootstep => SoundGroupRegistry.Get("sand");

    public TextureVariance TopVariance { get; private set; } = TextureVariance.None;
    public TextureVariance BottomVariance { get; private set; } = TextureVariance.None;
    public TextureVariance SideVariance { get; private set; } = TextureVariance.None;

    public IBlockTicker? Ticker { get; private set; }

    public IBlockInteractable? Interactable { get; private set; }

    public IBlockVisuals? Visuals { get; private set; }

    public IBlockLifecycle? Lifecycle { get; private set; }

    public IBlockPhysics? Physics { get; private set; }

    public IRedstoneComponent? Redstone { get; private set; }

    public virtual IReadOnlyList<string> GetBlockAlias => _blockAlias ?? [];

    protected Block IgnoreMetaUpdates()
    {
        BlocksIgnoreMetaUpdate[Id] = true;
        return this;
    }

    protected virtual void Init() => Lifecycle?.OnInit(this);

    protected Block setSoundGroup(BlockSoundGroup soundGroup)
    {
        SoundGroup = soundGroup;
        return this;
    }

    protected Block setOpacity(int opacity)
    {
        BlockLightOpacity[Id] = opacity;
        return this;
    }

    protected Block SetNonOpaque()
    {
        // The constructor caches isOpaque() into these arrays before fluent setters run,
        // so they must be refreshed here.
        _isOpaque = false;
        BlocksOpaque[Id] = false;
        BlockLightOpacity[Id] = 0;
        return this;
    }

    protected Block SetLuminance(float fractionalValue)
    {
        BlocksLightLuminance[Id] = (int)(15.0F * fractionalValue);
        return this;
    }

    protected Block SetResistance(float resistance)
    {
        this.Resistance = resistance * 3.0F;
        return this;
    }

    protected Block SetTopBottomTextures(int topTextureId, int bottomTextureId) => SetFaceTexture(Side.Up, topTextureId).SetFaceTexture(Side.Down, bottomTextureId);

    protected Block SetFaceTexture(Side side, int textureId)
    {
        _faceTextureIds ??= new int?[6];
        _faceTextureIds[(int)side] = textureId;
        return this;
    }

    /// <summary>
    ///     Configures the block's weighted drop table (e.g. gravel's flint chance). Item ids inside
    ///     each <see cref="LootEntry" /> are deferred, so they may safely reference another block's or item's
    ///     static field regardless of declaration order.
    /// </summary>
    protected Block SetLootTable(LootTable table, int minCount = 1, int maxCount = -1, int meta = 0)
    {
        _lootTable = table;
        _minDroppedCount = minCount;
        _maxDroppedCount = maxCount < 0 ? minCount : maxCount;
        _droppedItemMetaValue = meta;
        return this;
    }

    protected Block SetDropCount(int count)
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

    protected Block SetBlockAlias(params string[] aliases)
    {
        _blockAlias = aliases;
        return this;
    }

    public virtual bool IsFullCube() => _isFullCube;

    public virtual BlockRendererType GetRenderType() => _renderType;

    protected Block SetNotFullCube()
    {
        _isFullCube = false;
        return this;
    }

    protected Block SetRenderType(BlockRendererType renderType)
    {
        _renderType = renderType;
        return this;
    }

    /// <summary>Entities pass through this block (plants, portals, ...).</summary>
    protected Block SetNoCollision()
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

    protected Block SetHardness(float hardness)
    {
        this.Hardness = hardness;
        if (Resistance < hardness * 5.0F) Resistance = hardness * 5.0F;

        return this;
    }

    protected Block SetUnbreakable()
    {
        SetHardness(-1.0F);
        return this;
    }

    public float GetHardness() => Hardness;

    protected Block SetTickRandomly(bool tickRandomly)
    {
        BlocksRandomTick[Id] = tickRandomly;
        return this;
    }

    public Block SetBoundingBox(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        BoundingBox = new Box(minX, minY, minZ, maxX, maxY, maxZ);
        return this;
    }

    public virtual float GetLuminance(ILightProvider? lighting, int x, int y, int z)
    {
        float baseLuminance;
        if (lighting != null)
        {
            baseLuminance = lighting.GetNaturalBrightness(x, y, z, BlocksLightLuminance[Id]);
        }
        else
        {
            int baseLum = BlocksLightLuminance[Id];
            baseLuminance = baseLum > 0 ? baseLum / 15.0f : 1.0f;
        }

        return Visuals?.GetLuminance(this, lighting!, x, y, z, baseLuminance) ?? baseLuminance;
    }

    public virtual bool IsSideVisible(IBlockReader iBlockReader, int x, int y, int z, Side side)
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
        return Visuals?.IsSideVisible(this, iBlockReader, x, y, z, side, baseVisibility) ?? baseVisibility;
    }

    public virtual bool IsSolidFace(IBlockReader iBlockReader, int x, int y, int z, int face) => iBlockReader.GetMaterial(x, y, z).IsSolid;

    public virtual int GetTextureId(IBlockReader iBlockReader, int x, int y, int z, Side side)
    {
        int baseTexture = GetTexture(side, iBlockReader.GetBlockMeta(x, y, z));
        return Visuals?.GetTextureId(this, iBlockReader, x, y, z, side, baseTexture) ?? baseTexture;
    }

    public virtual int GetTexture(Side side, int meta)
    {
        int baseTexture = GetTexture(side);
        return Visuals?.GetTexture(this, side, meta, baseTexture) ?? baseTexture;
    }

    public virtual int GetTexture(Side side)
    {
        int baseTexture = _faceTextureIds?[(int)side] ?? TextureId;
        return Visuals?.GetTexture(this, side, baseTexture) ?? baseTexture;
    }

    public virtual Box GetBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        return BoundingBox.Offset(x, y, z);
    }

    public virtual void AddIntersectingBoundingBox(IBlockReader world, EntityManager entities, int x, int y, int z, Box box, List<Box> boxes)
    {
        if (Physics != null)
        {
            int countBefore = boxes.Count;
            Physics.AddCollisionBoxes(this, world, x, y, z, box, boxes);
            if (boxes.Count > countBefore)
            {
                return;
            }
        }

        Box? collisionBox = GetCollisionShape(world, entities, x, y, z);
        if (collisionBox != null && box.Intersects(collisionBox.Value))
        {
            boxes.Add(collisionBox.Value);
        }
    }

    public virtual Box? GetCollisionShape(IBlockReader world, EntityManager entities, int x, int y, int z)
    {
        UpdateBoundingBox(world, entities, x, y, z);
        Box? defaultShape = _hasCollision ? BoundingBox.Offset(x, y, z) : null;
        return Physics == null ? defaultShape : Physics.GetCollisionShape(this, world, entities, x, y, z, defaultShape);
    }

    public virtual bool IsOpaque() => Visuals?.IsOpaque(this, _isOpaque) ?? _isOpaque;

    public virtual bool HasCollision(int meta, bool allowLiquids) => Physics?.HasCollision(this, meta, allowLiquids, HasCollision()) ?? HasCollision();

    public virtual bool HasCollision() => Physics == null || Physics.HasCollision(this, true);

    public Block SetTicker(IBlockTicker ticker)
    {
        Ticker = ticker;
        return this;
    }

    public virtual void OnTick(OnTickEvent e) => Ticker?.OnTick(this, e);

    public virtual void RandomDisplayTick(OnTickEvent e) => Ticker?.RandomDisplayTick(this, e);

    public virtual void OnMetadataChange(OnMetadataChangeEvent ctx) => Lifecycle?.OnMetadataChange(this, ctx);

    public virtual void NeighborUpdate(OnTickEvent e) => Physics?.NeighborUpdate(this, e);

    public virtual int GetTickRate() => _tickRate;

    public virtual void OnPlaced(OnPlacedEvent e) => Lifecycle?.OnPlaced(this, e);

    protected Block SetTickRate(int rate)
    {
        _tickRate = rate;
        return this;
    }

    public virtual void OnBreak(OnBreakEvent e) => Lifecycle?.OnBreak(this, e);

    public virtual int GetDroppedItemCount()
    {
        int defaultCount = _minDroppedCount == _maxDroppedCount ? _minDroppedCount : _minDroppedCount + Random.Shared.Next(_maxDroppedCount - _minDroppedCount + 1);
        return Lifecycle?.GetDroppedItemCount(this, defaultCount) ?? defaultCount;
    }

    public virtual int GetDroppedItemId(int blockMeta)
    {
        int defaultId = _lootTable?.Roll(Random.Shared) ?? Id;
        return Lifecycle?.GetDroppedItemId(this, blockMeta, defaultId) ?? defaultId;
    }

    public float GetHardness(EntityPlayer player) => Hardness < 0.0F ? 0.0F : !player.CanHarvest(this) ? 1.0F / Hardness / 100.0F : player.GetBlockBreakingSpeed(this) / Hardness / 30.0F;

    public virtual void DropStacks(OnDropEvent ctx)
    {
        if (!ctx.World.IsRemote && ctx.World.Rules.GetBool(DefaultRules.DoTileDrops))
        {
            int dropCount = GetDroppedItemCount();

            for (int attempt = 0; attempt < dropCount; ++attempt)
            {
                if (!(Random.Shared.NextSingle() <= ctx.Luck)) continue;

                int itemId = GetDroppedItemId(ctx.Meta);
                if (itemId > 0)
                {
                    DropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(itemId, 1, GetDroppedItemMeta(ctx.Meta)));
                }
            }
        }

        Lifecycle?.OnDropStacks(this, ctx);
    }

    public static void DropStack(IWorldContext world, int x, int y, int z, ItemStack itemStack)
    {
        if (world.IsRemote || !world.Rules.GetBool(DefaultRules.DoTileDrops)) return;
        const float spreadFactor = 0.7F;
        double offsetX = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        double offsetY = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        double offsetZ = Random.Shared.NextSingle() * spreadFactor + (1.0F - spreadFactor) * 0.5D;
        world.SpawnItemDrop(x + offsetX, y + offsetY, z + offsetZ, itemStack);
    }

    protected virtual int GetDroppedItemMeta(int blockMeta)
    {
        int defaultMeta = _dropsWithBlockMeta ? blockMeta : _droppedItemMetaValue;
        return Lifecycle?.GetDroppedItemMeta(this, blockMeta, defaultMeta) ?? defaultMeta;
    }

    public virtual float GetBlastResistance(Entity entity) => Resistance / 5.0F;

    public virtual HitResult Raycast(IBlockReader world, EntityManager entities, int x, int y, int z, Vec3D startPos, Vec3D endPos)
    {
        UpdateBoundingBox(world, entities, x, y, z);
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

    public virtual void OnDestroyedByExplosion(OnDestroyedByExplosionEvent @event) => Lifecycle?.OnDestroyedByExplosion(this, @event);

    public virtual int GetRenderLayer() => _renderLayer;

    protected Block SetRenderLayer(int layer)
    {
        _renderLayer = layer;
        return this;
    }

    protected Block SetSlipperiness(float slipperiness)
    {
        Slipperiness = slipperiness;
        return this;
    }

    public virtual bool CanPlaceAt(CanPlaceAtContext evt)
    {
        int blockId = evt.World.Reader.GetBlockId(evt.X, evt.Y, evt.Z);
        bool baseResult = blockId == 0 || Blocks[blockId].Material.IsReplaceable;
        return Physics == null ? baseResult : baseResult && Physics.CanPlaceAt(this, evt);
    }

    public Block SetInteractable(IBlockInteractable interactable)
    {
        Interactable = interactable;
        return this;
    }

    public Block SetVisuals(IBlockVisuals visuals)
    {
        Visuals = visuals;
        return this;
    }

    public Block SetLifecycle(IBlockLifecycle lifecycle)
    {
        Lifecycle = lifecycle;
        return this;
    }

    public Block SetPhysics(IBlockPhysics physics)
    {
        Physics = physics;
        return this;
    }

    public Block SetRedstone(IRedstoneComponent redstone)
    {
        Redstone = redstone;
        return this;
    }

    public virtual bool OnUse(OnUseEvent ctx) => Interactable?.OnUse(this, ctx) ?? false;

    public virtual void onSteppedOn(OnEntityStepEvent @event) => Interactable?.OnSteppedOn(this, @event);

    public virtual void OnBlockBreakStart(OnBlockBreakStartEvent @event) => Interactable?.OnBlockBreakStart(this, @event);

    public virtual Vec3D ApplyVelocity(OnApplyVelocityEvent @event) => Physics?.ApplyVelocity(this, @event, Vec3D.Zero) ?? Vec3D.Zero;

    public void UpdateBoundingBox(IBlockReader blockReader, int x, int y, int z) => UpdateBoundingBox(blockReader, null, x, y, z);

    public virtual void UpdateBoundingBox(IBlockReader blockReader, EntityManager? entities, int x, int y, int z) => Physics?.UpdateBoundingBox(this, blockReader, entities, x, y, z);

    public virtual int GetColor(int meta) => Visuals?.GetColor(this, meta, 0xFFFFFF) ?? 0xFFFFFF;

    public virtual int GetColorForFace(int meta, int face)
    {
        int baseColor = GetColor(meta);
        return Visuals?.GetColorForFace(this, meta, face, baseColor) ?? baseColor;
    }

    public virtual int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z) => Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, 0xFFFFFF) ?? 0xFFFFFF;

    public virtual int GetColorMultiplier(IBlockReader iBlockReader, int x, int y, int z, int knownMeta)
    {
        int baseColor = GetColorMultiplier(iBlockReader, x, y, z);
        return Visuals?.GetColorMultiplier(this, iBlockReader, x, y, z, knownMeta, baseColor) ?? baseColor;
    }

    public virtual bool IsPoweringSide(IBlockReader iBlockReader, int x, int y, int z, int side) => Redstone != null && Redstone.IsPoweringSide(this, iBlockReader, x, y, z, side);

    public virtual bool CanEmitRedstonePower() => Redstone != null && Redstone.CanEmitRedstonePower(this);

    public virtual bool IsFlammable(IBlockReader iBlockReader, int x, int y, int z) => Physics != null && Physics.IsFlammable(this, iBlockReader, x, y, z, false);

    public virtual void OnEntityCollision(OnEntityCollisionEvent @event) => Interactable?.OnEntityCollision(this, @event);

    public virtual bool IsStrongPoweringSide(IBlockReader world, int x, int y, int z, int side) => Redstone != null && Redstone.IsStrongPoweringSide(this, world, x, y, z, side);

    public virtual void SetupRenderBoundingBox() => Physics?.SetupRenderBoundingBox(this);

    public virtual void OnAfterBreak(OnAfterBreakEvent ctx)
    {
        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[Id], 1);
        DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, ctx.Meta));
        Lifecycle?.OnAfterBreak(this, ctx);
    }

    public virtual bool CanGrow(OnTickEvent ctx) => Physics == null || Physics.CanGrow(this, ctx);

    public Block SetBlockName(string name)
    {
        _blockName = $"tile.{name}";
        return this;
    }

    public string TranslateBlockName() => Translations.Get($"{GetBlockName()}.name");

    public string GetBlockName() => _blockName;

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
                if (b is not null) s_registryNameToId.TryAdd(b.RegistryName, b.Id);
            }
        }

        return s_registryNameToId.TryGetValue(location.Path, out int id) ? Blocks[id] : null;
    }

    public virtual void OnBlockAction(OnBlockActionEvent ctx) => Lifecycle?.OnBlockAction(this, ctx);

    public bool GetEnableStats() => ShouldTrackStatistics;

    protected Block DisableStats()
    {
        ShouldTrackStatistics = false;
        return this;
    }

    public virtual PistonBehavior GetPistonBehavior() => _pistonBehaviorOverride ?? Material.PistonBehavior;

    /// <summary>Overrides the material-derived piston behavior (e.g. plates are destroyed when pushed).</summary>
    protected Block SetPistonBehavior(PistonBehavior behavior)
    {
        _pistonBehaviorOverride = behavior;
        return this;
    }

    /// <summary>
    ///     Declares that this block carries a tile entity, created by <paramref name="factory" />.
    ///     The factory is deferred, so it may safely reference types regardless of declaration order.
    /// </summary>
    protected Block SetHasTileEntity(Func<BlockEntity> factory)
    {
        BlocksWithEntity[Id] = true;
        _blockEntityFactory = factory;
        return this;
    }

    public virtual BlockEntity? GetBlockEntity() => _blockEntityFactory?.Invoke();
}
