using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Stats;
using betareborn.TileEntities;
using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.Blocks
{
    public class Block : java.lang.Object
    {
        public static readonly StepSound soundPowderFootstep = new("stone", 1.0F, 1.0F);
        public static readonly StepSound soundWoodFootstep = new("wood", 1.0F, 1.0F);
        public static readonly StepSound soundGravelFootstep = new("gravel", 1.0F, 1.0F);
        public static readonly StepSound soundGrassFootstep = new("grass", 1.0F, 1.0F);
        public static readonly StepSound soundStoneFootstep = new("stone", 1.0F, 1.0F);
        public static readonly StepSound soundMetalFootstep = new("stone", 1.0F, 1.5F);
        public static readonly StepSound soundGlassFootstep = new StepSoundStone("stone", 1.0F, 1.0F);
        public static readonly StepSound soundClothFootstep = new("cloth", 1.0F, 1.0F);
        public static readonly StepSound soundSandFootstep = new StepSoundSand("sand", 1.0F, 1.0F);
        public static readonly Block[] blocksList = new Block[256];
        public static readonly bool[] tickOnLoad = new bool[256];
        public static readonly bool[] opaqueCubeLookup = new bool[256];
        public static readonly bool[] isBlockContainer = new bool[256];
        public static readonly int[] lightOpacity = new int[256];
        public static readonly bool[] canBlockGrass = new bool[256];
        public static readonly int[] lightValue = new int[256];
        public static readonly bool[] field_28032_t = new bool[256];
        public static readonly Block stone = (new BlockStone(1, 1)).setHardness(1.5F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("stone");
        public static readonly BlockGrass grass = (BlockGrass)(new BlockGrass(2)).setHardness(0.6F).setStepSound(soundGrassFootstep).setBlockName("grass");
        public static readonly Block dirt = (new BlockDirt(3, 2)).setHardness(0.5F).setStepSound(soundGravelFootstep).setBlockName("dirt");
        public static readonly Block cobblestone = (new Block(4, 16, Material.rock)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("stonebrick");
        public static readonly Block planks = (new Block(5, 4, Material.wood)).setHardness(2.0F).setResistance(5.0F).setStepSound(soundWoodFootstep).setBlockName("wood").disableNeighborNotifyOnMetadataChange();
        public static readonly Block sapling = (new BlockSapling(6, 15)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("sapling").disableNeighborNotifyOnMetadataChange();
        public static readonly Block bedrock = (new Block(7, 17, Material.rock)).setBlockUnbreakable().setResistance(6000000.0F).setStepSound(soundStoneFootstep).setBlockName("bedrock").disableStats();
        public static readonly Block waterMoving = (new BlockFlowing(8, Material.water)).setHardness(100.0F).setLightOpacity(3).setBlockName("water").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block waterStill = (new BlockStationary(9, Material.water)).setHardness(100.0F).setLightOpacity(3).setBlockName("water").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block lavaMoving = (new BlockFlowing(10, Material.lava)).setHardness(0.0F).setLightValue(1.0F).setLightOpacity(255).setBlockName("lava").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block lavaStill = (new BlockStationary(11, Material.lava)).setHardness(100.0F).setLightValue(1.0F).setLightOpacity(255).setBlockName("lava").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block sand = (new BlockSand(12, 18)).setHardness(0.5F).setStepSound(soundSandFootstep).setBlockName("sand");
        public static readonly Block gravel = (new BlockGravel(13, 19)).setHardness(0.6F).setStepSound(soundGravelFootstep).setBlockName("gravel");
        public static readonly Block oreGold = (new BlockOre(14, 32)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreGold");
        public static readonly Block oreIron = (new BlockOre(15, 33)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreIron");
        public static readonly Block oreCoal = (new BlockOre(16, 34)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreCoal");
        public static readonly Block wood = (new BlockLog(17)).setHardness(2.0F).setStepSound(soundWoodFootstep).setBlockName("log").disableNeighborNotifyOnMetadataChange();
        public static readonly BlockLeaves leaves = (BlockLeaves)(new BlockLeaves(18, 52)).setHardness(0.2F).setLightOpacity(1).setStepSound(soundGrassFootstep).setBlockName("leaves").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block sponge = (new BlockSponge(19)).setHardness(0.6F).setStepSound(soundGrassFootstep).setBlockName("sponge");
        public static readonly Block glass = (new BlockGlass(20, 49, Material.glass, false)).setHardness(0.3F).setStepSound(soundGlassFootstep).setBlockName("glass");
        public static readonly Block oreLapis = (new BlockOre(21, 160)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreLapis");
        public static readonly Block blockLapis = (new Block(22, 144, Material.rock)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("blockLapis");
        public static readonly Block dispenser = (new BlockDispenser(23)).setHardness(3.5F).setStepSound(soundStoneFootstep).setBlockName("dispenser").disableNeighborNotifyOnMetadataChange();
        public static readonly Block sandStone = (new BlockSandStone(24)).setStepSound(soundStoneFootstep).setHardness(0.8F).setBlockName("sandStone");
        public static readonly Block musicBlock = (new BlockNote(25)).setHardness(0.8F).setBlockName("musicBlock").disableNeighborNotifyOnMetadataChange();
        public static readonly Block blockBed = (new BlockBed(26)).setHardness(0.2F).setBlockName("bed").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block railPowered = (new BlockRail(27, 179, true)).setHardness(0.7F).setStepSound(soundMetalFootstep).setBlockName("goldenRail").disableNeighborNotifyOnMetadataChange();
        public static readonly Block railDetector = (new BlockDetectorRail(28, 195)).setHardness(0.7F).setStepSound(soundMetalFootstep).setBlockName("detectorRail").disableNeighborNotifyOnMetadataChange();
        public static readonly Block pistonStickyBase = (new BlockPistonBase(29, 106, true)).setBlockName("pistonStickyBase").disableNeighborNotifyOnMetadataChange();
        public static readonly Block web = (new BlockWeb(30, 11)).setLightOpacity(1).setHardness(4.0F).setBlockName("web");
        public static readonly BlockTallGrass tallGrass = (BlockTallGrass)(new BlockTallGrass(31, 39)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("tallgrass");
        public static readonly BlockDeadBush deadBush = (BlockDeadBush)(new BlockDeadBush(32, 55)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("deadbush");
        public static readonly Block pistonBase = (new BlockPistonBase(33, 107, false)).setBlockName("pistonBase").disableNeighborNotifyOnMetadataChange();
        public static readonly BlockPistonExtension pistonExtension = (BlockPistonExtension)(new BlockPistonExtension(34, 107)).disableNeighborNotifyOnMetadataChange();
        public static readonly Block cloth = (new BlockCloth()).setHardness(0.8F).setStepSound(soundClothFootstep).setBlockName("cloth").disableNeighborNotifyOnMetadataChange();
        public static readonly BlockPistonMoving pistonMoving = new BlockPistonMoving(36);
        public static readonly BlockFlower plantYellow = (BlockFlower)(new BlockFlower(37, 13)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("flower");
        public static readonly BlockFlower plantRed = (BlockFlower)(new BlockFlower(38, 12)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("rose");
        public static readonly BlockFlower mushroomBrown = (BlockFlower)(new BlockMushroom(39, 29)).setHardness(0.0F).setStepSound(soundGrassFootstep).setLightValue(2.0F / 16.0F).setBlockName("mushroom");
        public static readonly BlockFlower mushroomRed = (BlockFlower)(new BlockMushroom(40, 28)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("mushroom");
        public static readonly Block blockGold = (new BlockOreStorage(41, 23)).setHardness(3.0F).setResistance(10.0F).setStepSound(soundMetalFootstep).setBlockName("blockGold");
        public static readonly Block blockSteel = (new BlockOreStorage(42, 22)).setHardness(5.0F).setResistance(10.0F).setStepSound(soundMetalFootstep).setBlockName("blockIron");
        public static readonly Block stairDouble = (new BlockStep(43, true)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("stoneSlab");
        public static readonly Block stairSingle = (new BlockStep(44, false)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("stoneSlab");
        public static readonly Block brick = (new Block(45, 7, Material.rock)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("brick");
        public static readonly Block tnt = (new BlockTNT(46, 8)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("tnt");
        public static readonly Block bookShelf = (new BlockBookshelf(47, 35)).setHardness(1.5F).setStepSound(soundWoodFootstep).setBlockName("bookshelf");
        public static readonly Block cobblestoneMossy = (new Block(48, 36, Material.rock)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("stoneMoss");
        public static readonly Block obsidian = (new BlockObsidian(49, 37)).setHardness(10.0F).setResistance(2000.0F).setStepSound(soundStoneFootstep).setBlockName("obsidian");
        public static readonly Block torchWood = (new BlockTorch(50, 80)).setHardness(0.0F).setLightValue(15.0F / 16.0F).setStepSound(soundWoodFootstep).setBlockName("torch").disableNeighborNotifyOnMetadataChange();
        public static readonly BlockFire fire = (BlockFire)(new BlockFire(51, 31)).setHardness(0.0F).setLightValue(1.0F).setStepSound(soundWoodFootstep).setBlockName("fire").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block mobSpawner = (new BlockMobSpawner(52, 65)).setHardness(5.0F).setStepSound(soundMetalFootstep).setBlockName("mobSpawner").disableStats();
        public static readonly Block stairCompactPlanks = (new BlockStairs(53, planks)).setBlockName("stairsWood").disableNeighborNotifyOnMetadataChange();
        public static readonly Block chest = (new BlockChest(54)).setHardness(2.5F).setStepSound(soundWoodFootstep).setBlockName("chest").disableNeighborNotifyOnMetadataChange();
        public static readonly Block redstoneWire = (new BlockRedstoneWire(55, 164)).setHardness(0.0F).setStepSound(soundPowderFootstep).setBlockName("redstoneDust").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block oreDiamond = (new BlockOre(56, 50)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreDiamond");
        public static readonly Block blockDiamond = (new BlockOreStorage(57, 24)).setHardness(5.0F).setResistance(10.0F).setStepSound(soundMetalFootstep).setBlockName("blockDiamond");
        public static readonly Block workbench = (new BlockWorkbench(58)).setHardness(2.5F).setStepSound(soundWoodFootstep).setBlockName("workbench");
        public static readonly Block crops = (new BlockCrops(59, 88)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("crops").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block tilledField = (new BlockFarmland(60)).setHardness(0.6F).setStepSound(soundGravelFootstep).setBlockName("farmland");
        public static readonly Block stoneOvenIdle = (new BlockFurnace(61, false)).setHardness(3.5F).setStepSound(soundStoneFootstep).setBlockName("furnace").disableNeighborNotifyOnMetadataChange();
        public static readonly Block stoneOvenActive = (new BlockFurnace(62, true)).setHardness(3.5F).setStepSound(soundStoneFootstep).setLightValue(14.0F / 16.0F).setBlockName("furnace").disableNeighborNotifyOnMetadataChange();
        public static readonly Block signPost = (new BlockSign(63, TileEntitySign.Class, true)).setHardness(1.0F).setStepSound(soundWoodFootstep).setBlockName("sign").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block doorWood = (new BlockDoor(64, Material.wood)).setHardness(3.0F).setStepSound(soundWoodFootstep).setBlockName("doorWood").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block ladder = (new BlockLadder(65, 83)).setHardness(0.4F).setStepSound(soundWoodFootstep).setBlockName("ladder").disableNeighborNotifyOnMetadataChange();
        public static readonly Block rail = (new BlockRail(66, 128, false)).setHardness(0.7F).setStepSound(soundMetalFootstep).setBlockName("rail").disableNeighborNotifyOnMetadataChange();
        public static readonly Block stairCompactCobblestone = (new BlockStairs(67, cobblestone)).setBlockName("stairsStone").disableNeighborNotifyOnMetadataChange();
        public static readonly Block signWall = (new BlockSign(68, TileEntitySign.Class, false)).setHardness(1.0F).setStepSound(soundWoodFootstep).setBlockName("sign").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block lever = (new BlockLever(69, 96)).setHardness(0.5F).setStepSound(soundWoodFootstep).setBlockName("lever").disableNeighborNotifyOnMetadataChange();
        public static readonly Block pressurePlateStone = (new BlockPressurePlate(70, stone.blockIndexInTexture, EnumMobType.mobs, Material.rock)).setHardness(0.5F).setStepSound(soundStoneFootstep).setBlockName("pressurePlate").disableNeighborNotifyOnMetadataChange();
        public static readonly Block doorSteel = (new BlockDoor(71, Material.iron)).setHardness(5.0F).setStepSound(soundMetalFootstep).setBlockName("doorIron").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block pressurePlatePlanks = (new BlockPressurePlate(72, planks.blockIndexInTexture, EnumMobType.everything, Material.wood)).setHardness(0.5F).setStepSound(soundWoodFootstep).setBlockName("pressurePlate").disableNeighborNotifyOnMetadataChange();
        public static readonly Block oreRedstone = (new BlockRedstoneOre(73, 51, false)).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreRedstone").disableNeighborNotifyOnMetadataChange();
        public static readonly Block oreRedstoneGlowing = (new BlockRedstoneOre(74, 51, true)).setLightValue(10.0F / 16.0F).setHardness(3.0F).setResistance(5.0F).setStepSound(soundStoneFootstep).setBlockName("oreRedstone").disableNeighborNotifyOnMetadataChange();
        public static readonly Block torchRedstoneIdle = (new BlockRedstoneTorch(75, 115, false)).setHardness(0.0F).setStepSound(soundWoodFootstep).setBlockName("notGate").disableNeighborNotifyOnMetadataChange();
        public static readonly Block torchRedstoneActive = (new BlockRedstoneTorch(76, 99, true)).setHardness(0.0F).setLightValue(0.5F).setStepSound(soundWoodFootstep).setBlockName("notGate").disableNeighborNotifyOnMetadataChange();
        public static readonly Block button = (new BlockButton(77, stone.blockIndexInTexture)).setHardness(0.5F).setStepSound(soundStoneFootstep).setBlockName("button").disableNeighborNotifyOnMetadataChange();
        public static readonly Block snow = (new BlockSnow(78, 66)).setHardness(0.1F).setStepSound(soundClothFootstep).setBlockName("snow");
        public static readonly Block ice = (new BlockIce(79, 67)).setHardness(0.5F).setLightOpacity(3).setStepSound(soundGlassFootstep).setBlockName("ice");
        public static readonly Block blockSnow = (new BlockSnowBlock(80, 66)).setHardness(0.2F).setStepSound(soundClothFootstep).setBlockName("snow");
        public static readonly Block cactus = (new BlockCactus(81, 70)).setHardness(0.4F).setStepSound(soundClothFootstep).setBlockName("cactus");
        public static readonly Block blockClay = (new BlockClay(82, 72)).setHardness(0.6F).setStepSound(soundGravelFootstep).setBlockName("clay");
        public static readonly Block reed = (new BlockReed(83, 73)).setHardness(0.0F).setStepSound(soundGrassFootstep).setBlockName("reeds").disableStats();
        public static readonly Block jukebox = (new BlockJukeBox(84, 74)).setHardness(2.0F).setResistance(10.0F).setStepSound(soundStoneFootstep).setBlockName("jukebox").disableNeighborNotifyOnMetadataChange();
        public static readonly Block fence = (new BlockFence(85, 4)).setHardness(2.0F).setResistance(5.0F).setStepSound(soundWoodFootstep).setBlockName("fence").disableNeighborNotifyOnMetadataChange();
        public static readonly Block pumpkin = (new BlockPumpkin(86, 102, false)).setHardness(1.0F).setStepSound(soundWoodFootstep).setBlockName("pumpkin").disableNeighborNotifyOnMetadataChange();
        public static readonly Block netherrack = (new BlockNetherrack(87, 103)).setHardness(0.4F).setStepSound(soundStoneFootstep).setBlockName("hellrock");
        public static readonly Block slowSand = (new BlockSoulSand(88, 104)).setHardness(0.5F).setStepSound(soundSandFootstep).setBlockName("hellsand");
        public static readonly Block glowStone = (new BlockGlowStone(89, 105, Material.rock)).setHardness(0.3F).setStepSound(soundGlassFootstep).setLightValue(1.0F).setBlockName("lightgem");
        public static readonly BlockPortal portal = (BlockPortal)(new BlockPortal(90, 14)).setHardness(-1.0F).setStepSound(soundGlassFootstep).setLightValue(12.0F / 16.0F).setBlockName("portal");
        public static readonly Block pumpkinLantern = (new BlockPumpkin(91, 102, true)).setHardness(1.0F).setStepSound(soundWoodFootstep).setLightValue(1.0F).setBlockName("litpumpkin").disableNeighborNotifyOnMetadataChange();
        public static readonly Block cake = (new BlockCake(92, 121)).setHardness(0.5F).setStepSound(soundClothFootstep).setBlockName("cake").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block redstoneRepeaterIdle = (new BlockRedstoneRepeater(93, false)).setHardness(0.0F).setStepSound(soundWoodFootstep).setBlockName("diode").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block redstoneRepeaterActive = (new BlockRedstoneRepeater(94, true)).setHardness(0.0F).setLightValue(10.0F / 16.0F).setStepSound(soundWoodFootstep).setBlockName("diode").disableStats().disableNeighborNotifyOnMetadataChange();
        public static readonly Block lockedChest = (new BlockLockedChest(95)).setHardness(0.0F).setLightValue(1.0F).setStepSound(soundWoodFootstep).setBlockName("lockedchest").setTickOnLoad(true).disableNeighborNotifyOnMetadataChange();
        public static readonly Block trapdoor = (new BlockTrapDoor(96, Material.wood)).setHardness(3.0F).setStepSound(soundWoodFootstep).setBlockName("trapdoor").disableStats().disableNeighborNotifyOnMetadataChange();
        public int blockIndexInTexture;
        public readonly int blockID;
        public float blockHardness;
        public float blockResistance;
        protected bool blockConstructorCalled;
        protected bool enableStats;
        public double minX;
        public double minY;
        public double minZ;
        public double maxX;
        public double maxY;
        public double maxZ;
        public StepSound stepSound;
        public float blockParticleGravity;
        public readonly Material blockMaterial;
        public float slipperiness;
        private string blockName;

        protected Block(int var1, Material var2)
        {
            blockConstructorCalled = true;
            enableStats = true;
            stepSound = soundPowderFootstep;
            blockParticleGravity = 1.0F;
            slipperiness = 0.6F;
            if (blocksList[var1] != null)
            {
                throw new IllegalArgumentException("Slot " + var1 + " is already occupied by " + blocksList[var1] + " when adding " + this);
            }
            else
            {
                blockMaterial = var2;
                blocksList[var1] = this;
                blockID = var1;
                setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                opaqueCubeLookup[var1] = isOpaqueCube();
                lightOpacity[var1] = isOpaqueCube() ? 255 : 0;
                canBlockGrass[var1] = !var2.getCanBlockGrass();
                isBlockContainer[var1] = false;
            }
        }

        protected Block disableNeighborNotifyOnMetadataChange()
        {
            field_28032_t[blockID] = true;
            return this;
        }

        protected virtual void initializeBlock()
        {
        }

        protected Block(int var1, int var2, Material var3) : this(var1, var3)
        {
            blockIndexInTexture = var2;
        }

        protected Block setStepSound(StepSound var1)
        {
            stepSound = var1;
            return this;
        }

        protected Block setLightOpacity(int var1)
        {
            lightOpacity[blockID] = var1;
            return this;
        }

        protected Block setLightValue(float var1)
        {
            lightValue[blockID] = (int)(15.0F * var1);
            return this;
        }

        protected Block setResistance(float var1)
        {
            blockResistance = var1 * 3.0F;
            return this;
        }

        public virtual bool renderAsNormalBlock()
        {
            return true;
        }

        public virtual int getRenderType()
        {
            return 0;
        }

        protected Block setHardness(float var1)
        {
            blockHardness = var1;
            if (blockResistance < var1 * 5.0F)
            {
                blockResistance = var1 * 5.0F;
            }

            return this;
        }

        protected Block setBlockUnbreakable()
        {
            setHardness(-1.0F);
            return this;
        }

        public float getHardness()
        {
            return blockHardness;
        }

        protected Block setTickOnLoad(bool var1)
        {
            tickOnLoad[blockID] = var1;
            return this;
        }

        public void setBlockBounds(float var1, float var2, float var3, float var4, float var5, float var6)
        {
            minX = (double)var1;
            minY = (double)var2;
            minZ = (double)var3;
            maxX = (double)var4;
            maxY = (double)var5;
            maxZ = (double)var6;
        }

        public virtual float getBlockBrightness(IBlockAccess var1, int var2, int var3, int var4)
        {
            return var1.getBrightness(var2, var3, var4, lightValue[blockID]);
        }

        public virtual bool shouldSideBeRendered(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return var5 == 0 && minY > 0.0D ? true : (var5 == 1 && maxY < 1.0D ? true : (var5 == 2 && minZ > 0.0D ? true : (var5 == 3 && maxZ < 1.0D ? true : (var5 == 4 && minX > 0.0D ? true : (var5 == 5 && maxX < 1.0D ? true : !var1.isBlockOpaqueCube(var2, var3, var4))))));
        }

        public virtual bool getIsBlockSolid(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return var1.getBlockMaterial(var2, var3, var4).isSolid();
        }

        public virtual int getBlockTexture(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return getBlockTextureFromSideAndMetadata(var5, var1.getBlockMetadata(var2, var3, var4));
        }

        public virtual int getBlockTextureFromSideAndMetadata(int var1, int var2)
        {
            return getBlockTextureFromSide(var1);
        }

        public virtual int getBlockTextureFromSide(int var1)
        {
            return blockIndexInTexture;
        }

        public virtual Box getSelectedBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return Box.createCached((double)var2 + minX, (double)var3 + minY, (double)var4 + minZ, (double)var2 + maxX, (double)var3 + maxY, (double)var4 + maxZ);
        }

        public virtual void getCollidingBoundingBoxes(World var1, int var2, int var3, int var4, Box var5, List<Box> var6)
        {
            Box var7 = getCollisionBoundingBoxFromPool(var1, var2, var3, var4);
            if (var7 != null && var5.intersects(var7))
            {
                var6.Add(var7);
            }

        }

        public virtual Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return Box.createCached((double)var2 + minX, (double)var3 + minY, (double)var4 + minZ, (double)var2 + maxX, (double)var3 + maxY, (double)var4 + maxZ);
        }

        public virtual bool isOpaqueCube()
        {
            return true;
        }

        public virtual bool canCollideCheck(int var1, bool var2)
        {
            return isCollidable();
        }

        public virtual bool isCollidable()
        {
            return true;
        }

        public virtual void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
        }

        public virtual void randomDisplayTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
        }

        public virtual void onBlockDestroyedByPlayer(World var1, int var2, int var3, int var4, int var5)
        {
        }

        public virtual void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
        }

        public virtual int tickRate()
        {
            return 10;
        }

        public virtual void onBlockAdded(World var1, int var2, int var3, int var4)
        {
        }

        public virtual void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
        }

        public virtual int quantityDropped(java.util.Random var1)
        {
            return 1;
        }

        public virtual int idDropped(int var1, java.util.Random var2)
        {
            return blockID;
        }

        public float blockStrength(EntityPlayer var1)
        {
            return blockHardness < 0.0F ? 0.0F : (!var1.canHarvestBlock(this) ? 1.0F / blockHardness / 100.0F : var1.getCurrentPlayerStrVsBlock(this) / blockHardness / 30.0F);
        }

        public void dropBlockAsItem(World var1, int var2, int var3, int var4, int var5)
        {
            dropBlockAsItemWithChance(var1, var2, var3, var4, var5, 1.0F);
        }

        public virtual void dropBlockAsItemWithChance(World var1, int var2, int var3, int var4, int var5, float var6)
        {
            if (!var1.multiplayerWorld)
            {
                int var7 = quantityDropped(var1.rand);

                for (int var8 = 0; var8 < var7; ++var8)
                {
                    if (var1.rand.nextFloat() <= var6)
                    {
                        int var9 = idDropped(var5, var1.rand);
                        if (var9 > 0)
                        {
                            dropBlockAsItem_do(var1, var2, var3, var4, new ItemStack(var9, 1, damageDropped(var5)));
                        }
                    }
                }

            }
        }

        protected void dropBlockAsItem_do(World var1, int var2, int var3, int var4, ItemStack var5)
        {
            if (!var1.multiplayerWorld)
            {
                float var6 = 0.7F;
                double var7 = (double)(var1.rand.nextFloat() * var6) + (double)(1.0F - var6) * 0.5D;
                double var9 = (double)(var1.rand.nextFloat() * var6) + (double)(1.0F - var6) * 0.5D;
                double var11 = (double)(var1.rand.nextFloat() * var6) + (double)(1.0F - var6) * 0.5D;
                EntityItem var13 = new EntityItem(var1, (double)var2 + var7, (double)var3 + var9, (double)var4 + var11, var5);
                var13.delayBeforeCanPickup = 10;
                var1.entityJoinedWorld(var13);
            }
        }

        protected virtual int damageDropped(int var1)
        {
            return 0;
        }

        public virtual float getExplosionResistance(Entity var1)
        {
            return blockResistance / 5.0F;
        }

        public virtual MovingObjectPosition collisionRayTrace(World var1, int var2, int var3, int var4, Vec3D var5, Vec3D var6)
        {
            setBlockBoundsBasedOnState(var1, var2, var3, var4);
            var5 = var5.addVector((double)(-var2), (double)(-var3), (double)(-var4));
            var6 = var6.addVector((double)(-var2), (double)(-var3), (double)(-var4));
            Vec3D var7 = var5.getIntermediateWithXValue(var6, minX);
            Vec3D var8 = var5.getIntermediateWithXValue(var6, maxX);
            Vec3D var9 = var5.getIntermediateWithYValue(var6, minY);
            Vec3D var10 = var5.getIntermediateWithYValue(var6, maxY);
            Vec3D var11 = var5.getIntermediateWithZValue(var6, minZ);
            Vec3D var12 = var5.getIntermediateWithZValue(var6, maxZ);
            if (!isVecInsideYZBounds(var7))
            {
                var7 = null;
            }

            if (!isVecInsideYZBounds(var8))
            {
                var8 = null;
            }

            if (!isVecInsideXZBounds(var9))
            {
                var9 = null;
            }

            if (!isVecInsideXZBounds(var10))
            {
                var10 = null;
            }

            if (!isVecInsideXYBounds(var11))
            {
                var11 = null;
            }

            if (!isVecInsideXYBounds(var12))
            {
                var12 = null;
            }

            Vec3D var13 = null;
            if (var7 != null && (var13 == null || var5.distanceTo(var7) < var5.distanceTo(var13)))
            {
                var13 = var7;
            }

            if (var8 != null && (var13 == null || var5.distanceTo(var8) < var5.distanceTo(var13)))
            {
                var13 = var8;
            }

            if (var9 != null && (var13 == null || var5.distanceTo(var9) < var5.distanceTo(var13)))
            {
                var13 = var9;
            }

            if (var10 != null && (var13 == null || var5.distanceTo(var10) < var5.distanceTo(var13)))
            {
                var13 = var10;
            }

            if (var11 != null && (var13 == null || var5.distanceTo(var11) < var5.distanceTo(var13)))
            {
                var13 = var11;
            }

            if (var12 != null && (var13 == null || var5.distanceTo(var12) < var5.distanceTo(var13)))
            {
                var13 = var12;
            }

            if (var13 == null)
            {
                return null;
            }
            else
            {
                int var14 = -1;
                if (var13 == var7)
                {
                    var14 = 4;
                }

                if (var13 == var8)
                {
                    var14 = 5;
                }

                if (var13 == var9)
                {
                    var14 = 0;
                }

                if (var13 == var10)
                {
                    var14 = 1;
                }

                if (var13 == var11)
                {
                    var14 = 2;
                }

                if (var13 == var12)
                {
                    var14 = 3;
                }

                return new MovingObjectPosition(var2, var3, var4, var14, var13.addVector((double)var2, (double)var3, (double)var4));
            }
        }

        private bool isVecInsideYZBounds(Vec3D var1)
        {
            return var1 == null ? false : var1.yCoord >= minY && var1.yCoord <= maxY && var1.zCoord >= minZ && var1.zCoord <= maxZ;
        }

        private bool isVecInsideXZBounds(Vec3D var1)
        {
            return var1 == null ? false : var1.xCoord >= minX && var1.xCoord <= maxX && var1.zCoord >= minZ && var1.zCoord <= maxZ;
        }

        private bool isVecInsideXYBounds(Vec3D var1)
        {
            return var1 == null ? false : var1.xCoord >= minX && var1.xCoord <= maxX && var1.yCoord >= minY && var1.yCoord <= maxY;
        }

        public virtual void onBlockDestroyedByExplosion(World var1, int var2, int var3, int var4)
        {
        }

        public virtual int getRenderBlockPass()
        {
            return 0;
        }

        public virtual bool canPlaceBlockOnSide(World var1, int var2, int var3, int var4, int var5)
        {
            return canPlaceBlockAt(var1, var2, var3, var4);
        }

        public virtual bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            int var5 = var1.getBlockId(var2, var3, var4);
            return var5 == 0 || blocksList[var5].blockMaterial.getIsGroundCover();
        }

        public virtual bool blockActivated(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            return false;
        }

        public virtual void onEntityWalking(World var1, int var2, int var3, int var4, Entity var5)
        {
        }

        public virtual void onBlockPlaced(World var1, int var2, int var3, int var4, int var5)
        {
        }

        public virtual void onBlockClicked(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
        }

        public virtual void velocityToAddToEntity(World var1, int var2, int var3, int var4, Entity var5, Vec3D var6)
        {
        }

        public virtual void setBlockBoundsBasedOnState(IBlockAccess var1, int var2, int var3, int var4)
        {
        }

        public virtual int getRenderColor(int var1)
        {
            return 16777215;
        }

        public virtual int colorMultiplier(IBlockAccess var1, int var2, int var3, int var4)
        {
            return 16777215;
        }

        public virtual bool isPoweringTo(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return false;
        }

        public virtual bool canProvidePower()
        {
            return false;
        }

        public virtual void onEntityCollidedWithBlock(World var1, int var2, int var3, int var4, Entity var5)
        {
        }

        public virtual bool isIndirectlyPoweringTo(World var1, int var2, int var3, int var4, int var5)
        {
            return false;
        }

        public virtual void setBlockBoundsForItemRender()
        {
        }

        public virtual void harvestBlock(World var1, EntityPlayer var2, int var3, int var4, int var5, int var6)
        {
            var2.addStat(StatList.mineBlockStatArray[blockID], 1);
            dropBlockAsItem(var1, var3, var4, var5, var6);
        }

        public virtual bool canBlockStay(World var1, int var2, int var3, int var4)
        {
            return true;
        }

        public virtual void onBlockPlacedBy(World var1, int var2, int var3, int var4, EntityLiving var5)
        {
        }

        public Block setBlockName(string var1)
        {
            blockName = "tile." + var1;
            return this;
        }

        public string translateBlockName()
        {
            return StatCollector.translateToLocal(getBlockName() + ".name");
        }

        public string getBlockName()
        {
            return blockName;
        }

        public virtual void playBlock(World var1, int var2, int var3, int var4, int var5, int var6)
        {
        }

        public bool getEnableStats()
        {
            return enableStats;
        }

        protected Block disableStats()
        {
            enableStats = false;
            return this;
        }

        public virtual int getMobilityFlag()
        {
            return blockMaterial.getMaterialMobility();
        }

        static Block()
        {
            Item.itemsList[cloth.blockID] = (new ItemCloth(cloth.blockID - 256)).setItemName("cloth");
            Item.itemsList[wood.blockID] = (new ItemLog(wood.blockID - 256)).setItemName("log");
            Item.itemsList[stairSingle.blockID] = (new ItemSlab(stairSingle.blockID - 256)).setItemName("stoneSlab");
            Item.itemsList[sapling.blockID] = (new ItemSapling(sapling.blockID - 256)).setItemName("sapling");
            Item.itemsList[leaves.blockID] = (new ItemLeaves(leaves.blockID - 256)).setItemName("leaves");
            Item.itemsList[pistonBase.blockID] = new ItemPiston(pistonBase.blockID - 256);
            Item.itemsList[pistonStickyBase.blockID] = new ItemPiston(pistonStickyBase.blockID - 256);

            for (int var0 = 0; var0 < 256; ++var0)
            {
                if (blocksList[var0] != null && Item.itemsList[var0] == null)
                {
                    Item.itemsList[var0] = new ItemBlock(var0 - 256);
                    blocksList[var0].initializeBlock();
                }
            }

            canBlockGrass[0] = true;
            StatList.func_25154_a();
        }
    }

}