using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Gen.Flat;

internal class FlatChunkGenerator : IChunkSource
{
    private readonly CactusPatchFeature _featureCactus = new();
    private readonly ClayOreFeature _featureClay = new(32);
    private readonly DungeonFeature _featureDungeon = new();
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
    {
        _world = world;
        _generatorInfo = FlatGeneratorInfo.CreateFromString(generatorOptions, world.Content.Blocks);
        _random = new JavaRandom(world.Seed);
        InitFeatures();
    }

    public IChunkSource CreateParallelInstance() => new FlatChunkGenerator(_world);

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
        _featureWaterLake = new LakeFeature(_world.Content.Blocks.Get("water").Id);
        _featureLavaLake = new LakeFeature(_world.Content.Blocks.Get("lava").Id);
        _featureDirt = new OreFeature(_world.Content.Blocks.Get("dirt").Id, 32);
        _featureGravel = new OreFeature(_world.Content.Blocks.Get("gravel").Id, 32);
        _featureCoal = new OreFeature(_world.Content.Blocks.Get("coal_ore").Id, 16);
        _featureIron = new OreFeature(_world.Content.Blocks.Get("iron_ore").Id, 8);
        _featureGold = new OreFeature(_world.Content.Blocks.Get("gold_ore").Id, 8);
        _featureRedstone = new OreFeature(_world.Content.Blocks.Get("redstone_ore").Id, 7);
        _featureDiamond = new OreFeature(_world.Content.Blocks.Get("diamond_ore").Id, 7);
        _featureLapis = new OreFeature(_world.Content.Blocks.Get("lapis_ore").Id, 6);
        _featureDandelion = new PlantPatchFeature(_world.Content.Blocks.Get("dandelion").Id);
        _featureRose = new PlantPatchFeature(_world.Content.Blocks.Get("rose").Id);
        _featureBrownMushroom = new PlantPatchFeature(_world.Content.Blocks.Get("brown_mushroom").Id);
        _featureRedMushroom = new PlantPatchFeature(_world.Content.Blocks.Get("red_mushroom").Id);
        _featureDeadBush = new DeadBushPatchFeature(_world.Content.Blocks.Get("dead_bush").Id);
        _featureGrass = new GrassPatchFeature(_world.Content.Blocks.Get("grass").Id, 1);
        _featureWaterSpring = new SpringFeature(_world.Content.Blocks.Get("flowing_water").Id);
        _featureLavaSpring = new SpringFeature(_world.Content.Blocks.Get("flowing_lava").Id);
    }
}
