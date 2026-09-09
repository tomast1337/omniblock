using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Gen.Flat;

internal class FlatChunkGenerator : IChunkSource
{
    private readonly BlockIds _blocks;
    private readonly CactusPatchFeature _featureCactus = new();
    private readonly ClayOreFeature _featureClay = new(32);
    private readonly DungeonFeature _featureDungeon = new();
    private readonly string _generatorOptions;
    private readonly PumpkinPatchFeature _featurePumpkin = new();
    private readonly SugarCanePatchFeature _featureSugarcane = new();
    private readonly FlatGeneratorInfo _generatorInfo;
    private readonly JavaRandom _random;
    private readonly IWorldContext _world;
    private PlantPatchFeature _featureBrownMushroom;
    private OreFeature _featureCoal;
    private PlantPatchFeature _featureDandelion;
    private DeadBushPatchFeature _featureDeadBush;
    private OreFeature _featureDiamond;
    private OreFeature _featureDirt;
    private OreFeature _featureGold;
    private GrassPatchFeature _featureGrass;
    private OreFeature _featureGravel;
    private OreFeature _featureIron;
    private OreFeature _featureLapis;
    private LakeFeature _featureLavaLake;
    private SpringFeature _featureLavaSpring;
    private PlantPatchFeature _featureRedMushroom;
    private OreFeature _featureRedstone;
    private PlantPatchFeature _featureRose;

    private LakeFeature _featureWaterLake;
    private SpringFeature _featureWaterSpring;

    public FlatChunkGenerator(IWorldContext world) : this(world, world.Properties.GeneratorOptions)
    {
    }

    public FlatChunkGenerator(IWorldContext world, string generatorOptions)
        : this(world, generatorOptions, BlockIds.Resolve(world))
    {
    }

    private FlatChunkGenerator(IWorldContext world, string generatorOptions, BlockIds blocks)
    {
        _world = world;
        _blocks = blocks;
        _generatorOptions = generatorOptions;
        _generatorInfo = FlatGeneratorInfo.CreateFromString(generatorOptions, world.Content.Blocks);
        _random = new JavaRandom(world.Seed);
        InitFeatures();
    }

    public IChunkSource CreateParallelInstance() =>
        new FlatChunkGenerator(_world, _generatorOptions, _blocks);

    public Chunk GetChunk(int chunkX, int chunkZ)
    {
        var blockL = ChuckFormat.ChunkSize;
        var blocks = new byte[blockL];
        ChunkNibbleArray meta = new(blockL);

        foreach (var layer in _generatorInfo.FlatLayers)
        {
            var blockId = layer.FillBlock;
            var blockMeta = layer.FillBlockMeta;

            for (var y = layer.MinY; (y < layer.MinY + layer.LayerCount) & (y < ChuckFormat.WorldHeight); ++y)
            {
                for (var x = 0; x < 16; ++x)
                {
                    for (var z = 0; z < 16; ++z)
                    {
                        var index = ChuckFormat.GetIndex(x, y, z);
                        blocks[index] = (byte)blockId;
                        if (blockMeta > 0)
                        {
                            meta.SetNibble(x, y, z, blockMeta);
                        }
                    }
                }
            }
        }

        Chunk chunk = new(_world, blocks, chunkX, chunkZ)
        {
            Meta = meta
        };

        chunk.PopulateHeightMap();
        return chunk;
    }

    public bool Save(bool bl, LoadingDisplay loadingDisplay) => true;

    public bool CanSave() => true;

    public bool IsChunkLoaded(int x, int z) => true;
    public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

    public void DecorateTerrain(IChunkSource chunkSource, int chunkX, int chunkZ)
    {
        var blockX = chunkX * 16;
        var blockZ = chunkZ * 16;
        var chunkBiome = Biome.Plains; // Default

        _random.SetSeed(_world.Seed);
        var xOffset = _random.NextLong() / 2L * 2L + 1L;
        var zOffset = _random.NextLong() / 2L * 2L + 1L;
        _random.SetSeed((chunkX * xOffset + chunkZ * zOffset) ^ _world.Seed);

        int featureX;
        int featureY;
        int featureZ;

        var hasLakes = _generatorInfo.WorldFeatures.ContainsKey("lake");
        var hasLavaLakes = _generatorInfo.WorldFeatures.ContainsKey("lava_lake");
        var hasDungeons = _generatorInfo.WorldFeatures.ContainsKey("dungeon");
        var hasDecoration = _generatorInfo.WorldFeatures.ContainsKey("decoration");

        if (hasLakes && _random.NextInt(4) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureWaterLake.Generate(_world, _random, featureX, featureY, featureZ);
        }

        if (hasLavaLakes && _random.NextInt(8) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(_random.NextInt(120) + 8);
            featureZ = blockZ + _random.NextInt(16) + 8;
            if (featureY < 64 || _random.NextInt(10) == 0)
            {
                _featureLavaLake.Generate(_world, _random, featureX, featureY, featureZ);
            }
        }

        if (hasDungeons)
        {
            for (var i = 0; i < 8; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureDungeon.Generate(_world, _random, featureX, featureY, featureZ);
            }
        }

        if (hasDecoration)
        {
            // Ores
            for (var i = 0; i < 10; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureClay.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 20; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureDirt.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 10; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureGravel.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 20; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureCoal.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 20; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(64);
                featureZ = blockZ + _random.NextInt(16);
                _featureIron.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 2; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(32);
                featureZ = blockZ + _random.NextInt(16);
                _featureGold.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 8; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(16);
                featureZ = blockZ + _random.NextInt(16);
                _featureRedstone.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 1; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(16);
                featureZ = blockZ + _random.NextInt(16);
                _featureDiamond.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 1; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(16) + _random.NextInt(16);
                featureZ = blockZ + _random.NextInt(16);
                _featureLapis.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Trees
            var numberOfTrees = 0;
            if (_random.NextInt(10) == 0) numberOfTrees++;

            for (var i = 0; i < numberOfTrees; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureZ = blockZ + _random.NextInt(16) + 8;
                var treeFeature = chunkBiome.GetRandomWorldGenForTrees(_random);
                treeFeature.prepare(1.0D, 1.0D, 1.0D);
                treeFeature.Generate(_world, _random, featureX, _world.Reader.GetTopY(featureX, featureZ), featureZ);
            }

            // Flowers and Mushrooms
            for (var i = 0; i < 2; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureDandelion.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(2) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureRose.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(4) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureBrownMushroom.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(8) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureRedMushroom.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Sugarcane, Pumpkins
            for (var i = 0; i < 10; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureSugarcane.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(32) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featurePumpkin.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Grass, Dead Bush, Cactus
            for (var i = 0; i < 20; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureGrass.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 2; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureDeadBush.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 10; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureCactus.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Spring Features
            for (var i = 0; i < 50; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(_random.NextInt(120) + 8);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureWaterSpring.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < 20; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(_random.NextInt(_random.NextInt(112) + 8) + 8);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureLavaSpring.Generate(_world, _random, featureX, featureY, featureZ);
            }
        }
    }

    public bool Tick() => false;
    public string GetDebugInfo() => "FlatLevelSource";

    private void InitFeatures()
    {
        _featureWaterLake = new LakeFeature(_blocks.Water);
        _featureLavaLake = new LakeFeature(_blocks.Lava);
        _featureDirt = new OreFeature(_blocks.Dirt, 32);
        _featureGravel = new OreFeature(_blocks.Gravel, 32);
        _featureCoal = new OreFeature(_blocks.CoalOre, 16);
        _featureIron = new OreFeature(_blocks.IronOre, 8);
        _featureGold = new OreFeature(_blocks.GoldOre, 8);
        _featureRedstone = new OreFeature(_blocks.RedstoneOre, 7);
        _featureDiamond = new OreFeature(_blocks.DiamondOre, 7);
        _featureLapis = new OreFeature(_blocks.LapisOre, 6);
        _featureDandelion = new PlantPatchFeature(_blocks.Dandelion);
        _featureRose = new PlantPatchFeature(_blocks.Rose);
        _featureBrownMushroom = new PlantPatchFeature(_blocks.BrownMushroom);
        _featureRedMushroom = new PlantPatchFeature(_blocks.RedMushroom);
        _featureDeadBush = new DeadBushPatchFeature(_blocks.DeadBush);
        _featureGrass = new GrassPatchFeature(_blocks.Grass, 1);
        _featureWaterSpring = new SpringFeature(_blocks.FlowingWater);
        _featureLavaSpring = new SpringFeature(_blocks.FlowingLava);
    }

    private sealed class BlockIds
    {
        private BlockIds(IWorldContext world)
        {
            var blocks = world.Content.Blocks;
            Water = blocks.Get("water").Id;
            FlowingWater = blocks.Get("flowing_water").Id;
            Lava = blocks.Get("lava").Id;
            FlowingLava = blocks.Get("flowing_lava").Id;
            Dirt = blocks.Get("dirt").Id;
            Gravel = blocks.Get("gravel").Id;
            CoalOre = blocks.Get("coal_ore").Id;
            IronOre = blocks.Get("iron_ore").Id;
            GoldOre = blocks.Get("gold_ore").Id;
            RedstoneOre = blocks.Get("redstone_ore").Id;
            DiamondOre = blocks.Get("diamond_ore").Id;
            LapisOre = blocks.Get("lapis_ore").Id;
            Dandelion = blocks.Get("dandelion").Id;
            Rose = blocks.Get("rose").Id;
            BrownMushroom = blocks.Get("brown_mushroom").Id;
            RedMushroom = blocks.Get("red_mushroom").Id;
            DeadBush = blocks.Get("dead_bush").Id;
            Grass = blocks.Get("grass").Id;
        }

        public int Water { get; }
        public int FlowingWater { get; }
        public int Lava { get; }
        public int FlowingLava { get; }
        public int Dirt { get; }
        public int Gravel { get; }
        public int CoalOre { get; }
        public int IronOre { get; }
        public int GoldOre { get; }
        public int RedstoneOre { get; }
        public int DiamondOre { get; }
        public int LapisOre { get; }
        public int Dandelion { get; }
        public int Rose { get; }
        public int BrownMushroom { get; }
        public int RedMushroom { get; }
        public int DeadBush { get; }
        public int Grass { get; }

        public static BlockIds Resolve(IWorldContext world) => new(world);
    }
}
