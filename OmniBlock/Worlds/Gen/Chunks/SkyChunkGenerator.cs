using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths.Noise;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Worlds.Generation.Generators.Carvers;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Gen.Chunks;

internal class SkyChunkGenerator : CommonChunkGenerator, IChunkSource
{
    private readonly BlockIds _blocks;
    private readonly BiomeSource _biomeSource;
    private readonly Carver _carver = new CaveCarver();
    private readonly OctavePerlinNoiseSampler _depthNoise;
    private readonly CactusPatchFeature _featureCactus = new();
    private readonly ClayOreFeature _featureClay = new(32);
    private readonly DungeonFeature _featureDungeon = new();
    private readonly PumpkinPatchFeature _featurePumpkin = new();
    private readonly SugarCanePatchFeature _featureSugarcane = new();
    private readonly OctavePerlinNoiseSampler _floatingIslandNoise;
    private readonly OctavePerlinNoiseSampler _floatingIslandScale;
    private readonly OctavePerlinNoiseSampler _forestNoise;
    private readonly OctavePerlinNoiseSampler _maxLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _minLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _selectorNoise;
    private Biome[] _biomes;
    private double[] _depthBuffer = new double[256];
    private double[] _depthNoiseBuffer;
    private PlantPatchFeature _featureBrownMushroom;
    private OreFeature _featureCoal;
    private PlantPatchFeature _featureDandelion;
    private OreFeature _featureDiamond;
    private OreFeature _featureDirt;
    private OreFeature _featureGold;
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
    private double[] _heightMap;
    private double[] _maxLimitPerlinNoiseBuffer;
    private double[] _minLimitPerlinNoiseBuffer;
    private double[] _scaleNoiseBuffer;
    private double[] _selectorNoiseBuffer;
    private double[] _temperatures;

    public SkyChunkGenerator(IWorldContext world, long seed)
        : this(world, seed, world.Dimension.BiomeSource, BlockIds.Resolve(world.Content.Blocks))
    {
    }

    internal SkyChunkGenerator(IWorldContext world, long seed, BlockIds blocks)
        : this(world, seed, world.Dimension.BiomeSource, blocks)
    {
    }

    private SkyChunkGenerator(
        IWorldContext world,
        long seed,
        BiomeSource biomeSource,
        BlockIds blocks) : base(world, seed)
    {
        _blocks = blocks;
        _biomeSource = biomeSource;
        _minLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, 16);
        _maxLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, 16);
        _selectorNoise = new OctavePerlinNoiseSampler(_random, 8);
        _depthNoise = new OctavePerlinNoiseSampler(_random, 4);
        _floatingIslandScale = new OctavePerlinNoiseSampler(_random, 10);
        _floatingIslandNoise = new OctavePerlinNoiseSampler(_random, 16);
        _forestNoise = new OctavePerlinNoiseSampler(_random, 8);
        InitFeatures();
    }

    public IChunkSource CreateParallelInstance() => new SkyChunkGenerator(_world, _seed, _biomeSource.Clone(), _blocks);

    public Chunk LoadChunk(int chunkX, int chunkZ) => GetChunk(chunkX, chunkZ);

    public Chunk GetChunk(int chunkX, int chunkZ)
    {
        _random.SetSeed(chunkX * 341873128712L + chunkZ * 132897987541L);
        var blocks = new byte[ChuckFormat.ChunkSize];
        Chunk chunk = new(_world, blocks, chunkX, chunkZ);
        _biomes = _biomeSource.GetBiomesInArea(_biomes, chunkX * 16, chunkZ * 16, 16, 16);
        BuildTerrain(chunkX, chunkZ, blocks);
        BuildSurfaces(chunkX, chunkZ, blocks, _biomes);
        _carver.carve(this, _world, chunkX, chunkZ, blocks);
        chunk.PopulateHeightMap();
        return chunk;
    }

    public bool IsChunkLoaded(int chunkX, int chunkZ) => true;

    public void DecorateTerrain(IChunkSource source, int chunkX, int chunkZ)
    {
        FallingBlockBehavior.FallInstantly = true;
        var blockX = chunkX * 16;
        var blockZ = chunkZ * 16;
        var chunkBiome = _biomeSource.GetBiome(blockX + 16, blockZ + 16);
        _random.SetSeed(_world.Seed);
        var xOffset = _random.NextLong() / 2L * 2L + 1L;
        var zOffset = _random.NextLong() / 2L * 2L + 1L;
        _random.SetSeed((chunkX * xOffset + chunkZ * zOffset) ^ _world.Seed);
        double fraction;
        int featureX;
        int featureY;
        int featureZ;

        if (_random.NextInt(4) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureWaterLake.Generate(_world, _random, featureX, featureY, featureZ);
        }

        if (_random.NextInt(8) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(_random.NextInt(120) + 8);
            featureZ = blockZ + _random.NextInt(16) + 8;
            if (featureY < 64 || _random.NextInt(10) == 0)
            {
                _featureLavaLake.Generate(_world, _random, featureX, featureY, featureZ);
            }
        }

        for (var i = 0; i < 8; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureDungeon.Generate(_world, _random, featureX, featureY, featureZ);
        }

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

        fraction = 0.5D;
        var treeDensitySample = (int)((_forestNoise.GenerateNoise(blockX * fraction, blockZ * fraction) / 8.0D + _random.NextDouble() * 4.0D + 4.0D) / 3.0D);
        var numberOfTrees = 0;

        if (_random.NextInt(10) == 0)
        {
            ++numberOfTrees;
        }

        if (chunkBiome == Biome.Forest)
        {
            numberOfTrees += treeDensitySample + 5;
        }

        if (chunkBiome == Biome.Rainforest)
        {
            numberOfTrees += treeDensitySample + 5;
        }

        if (chunkBiome == Biome.SeasonalForest)
        {
            numberOfTrees += treeDensitySample + 2;
        }

        if (chunkBiome == Biome.Taiga)
        {
            numberOfTrees += treeDensitySample + 5;
        }

        if (chunkBiome == Biome.Desert)
        {
            numberOfTrees -= 20;
        }

        if (chunkBiome == Biome.Tundra)
        {
            numberOfTrees -= 20;
        }

        if (chunkBiome == Biome.Plains)
        {
            numberOfTrees -= 20;
        }

        for (var i = 0; i < numberOfTrees; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureZ = blockZ + _random.NextInt(16) + 8;
            var treeFeature = chunkBiome.GetRandomWorldGenForTrees(_random);
            treeFeature.prepare(1.0D, 1.0D, 1.0D);
            treeFeature.Generate(_world, _random, featureX, _world.Reader.GetTopY(featureX, featureZ), featureZ);
        }

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

        var amountOfCacti = 0;
        if (chunkBiome == Biome.Desert)
        {
            amountOfCacti += 10;
        }

        for (var i = 0; i < amountOfCacti; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureCactus.Generate(_world, _random, featureX, featureY, featureZ);
        }

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

        _temperatures = _biomeSource.GetTemperatures(_temperatures, blockX + 8, blockZ + 8, 16, 16);

        for (var x = blockX + 8; x < blockX + 8 + 16; ++x)
        {
            for (var z = blockZ + 8; z < blockZ + 8 + 16; ++z)
            {
                var offsetX = x - (blockX + 8);
                var offsetZ = z - (blockZ + 8);
                var topBlockY = _world.Reader.GetTopSolidBlockY(x, z);
                var temperatureSample = _temperatures[offsetX * 16 + offsetZ] - (topBlockY - 64) / 64.0D * 0.3D;

                if (temperatureSample < 0.5D && topBlockY > 0 && topBlockY < 128 && _world.Reader.IsAir(x, topBlockY, z) && _world.Reader.GetMaterial(x, topBlockY - 1, z).BlocksMovement && _world.Reader.GetMaterial(x, topBlockY - 1, z) != Material.Ice)
                {
                    _world.Writer.SetBlock(x, topBlockY, z, _blocks.Snow);
                }
            }
        }

        FallingBlockBehavior.FallInstantly = false;
    }

    public bool Save(bool b, LoadingDisplay display) => true;

    public bool Tick() => false;

    public bool CanSave() => true;

    public string GetDebugInfo() => "RandomLevelSource";

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
        _featureWaterSpring = new SpringFeature(_blocks.FlowingWater);
        _featureLavaSpring = new SpringFeature(_blocks.FlowingLava);
    }

    public void BuildTerrain(int chunkX, int chunkZ, byte[] blocks)
    {
        byte horiScale = 2;
        var xMax = horiScale + 1;
        byte yMax = 128 / 4 + 1;
        var zMax = horiScale + 1;

        _heightMap = GenerateHeightMap(_heightMap, chunkX * horiScale, 0, chunkZ * horiScale, xMax, yMax, zMax);

        for (var sampleX = 0; sampleX < horiScale; ++sampleX)
        {
            for (var sampleZ = 0; sampleZ < horiScale; ++sampleZ)
            {
                for (var sampleY = 0; sampleY < 32; ++sampleY)
                {
                    var verticalLerpStep = 0.25D;
                    var corner000 = _heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner010 = _heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner100 = _heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner110 = _heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner001 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner000) * verticalLerpStep;
                    var corner011 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner010) * verticalLerpStep;
                    var corner101 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner100) * verticalLerpStep;
                    var corner111 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner110) * verticalLerpStep;

                    for (var subY = 0; subY < 4; ++subY)
                    {
                        var horizontalLerpStep = 0.125D;
                        var terrainX0 = corner000;
                        var terrainX1 = corner010;
                        var terrainStepX0 = (corner100 - corner000) * horizontalLerpStep;
                        var terrainStepX1 = (corner110 - corner010) * horizontalLerpStep;

                        for (var subX = 0; subX < 8; ++subX)
                        {
                            var blockIndex = ChuckFormat.GetIndex(subX + sampleX * 8, sampleY * 4 + subY, sampleZ * 8);
                            var horizontalLerpStepZ = 0.125D;
                            var terrainDensity = terrainX0;
                            var densityStepZ = (terrainX1 - terrainX0) * horizontalLerpStepZ;

                            for (var subZ = 0; subZ < 8; ++subZ)
                            {
                                var blockType = 0;
                                if (terrainDensity > 0.0D)
                                {
                                    blockType = _blocks.Stone;
                                }

                                blocks[blockIndex] = (byte)blockType;
                                blockIndex += ChuckFormat.ChunkHeight;
                                terrainDensity += densityStepZ;
                            }

                            terrainX0 += terrainStepX0;
                            terrainX1 += terrainStepX1;
                        }

                        corner000 += corner001;
                        corner010 += corner011;
                        corner100 += corner101;
                        corner110 += corner111;
                    }
                }
            }
        }
    }

    public void BuildSurfaces(int chunkX, int chunkZ, byte[] blocks, Biome[] biomes)
    {
        var chunkBiome = 1.0D / 32.0D;
        _depthBuffer = _depthNoise.Create(_depthBuffer, chunkX * 16, chunkZ * 16, 0.0D, 16, 16, 1, chunkBiome * 2.0D, chunkBiome * 2.0D, chunkBiome * 2.0D);

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var localBiome = biomes[localX + localZ * 16];
                var surfaceDepth = (int)(_depthBuffer[localX + localZ * 16] / 3.0D + 3.0D + _random.NextDouble() * 0.25D);
                var currentDepth = -1;
                var topBlock = localBiome.TopBlockId;
                var soilBlock = localBiome.SoilBlockId;

                for (var blockY = 127; blockY >= 0; --blockY)
                {
                    var blockIndex = (localZ * 16 + localX) * 128 + blockY;
                    var currentBlock = blocks[blockIndex];
                    if (currentBlock == 0)
                    {
                        currentDepth = -1;
                    }
                    else if (currentBlock == _blocks.Stone)
                    {
                        if (currentDepth == -1)
                        {
                            if (surfaceDepth <= 0)
                            {
                                topBlock = 0;
                                soilBlock = (byte)_blocks.Stone;
                            }

                            currentDepth = surfaceDepth;
                            if (blockY >= 0)
                            {
                                blocks[blockIndex] = topBlock;
                            }
                            else
                            {
                                blocks[blockIndex] = soilBlock;
                            }
                        }
                        else if (currentDepth > 0)
                        {
                            --currentDepth;
                            blocks[blockIndex] = soilBlock;
                            if (currentDepth == 0 && soilBlock == _blocks.Sand)
                            {
                                currentDepth = _random.NextInt(4);
                                soilBlock = (byte)_blocks.Sandstone;
                            }
                        }
                    }
                }
            }
        }
    }

    private double[] GenerateHeightMap(double[]? heightMap, int x, int y, int z, int sizeX, int sizeY, int sizeZ)
    {
        heightMap ??= new double[sizeX * sizeY * sizeZ];

        var horizontalScale = 684.412D;
        var verticalScale = 684.412D;
        _scaleNoiseBuffer = _floatingIslandScale.Create(_scaleNoiseBuffer, x, z, sizeX, sizeZ, 1.121D, 1.121D, 0.5D);
        _depthNoiseBuffer = _floatingIslandNoise.Create(_depthNoiseBuffer, x, z, sizeX, sizeZ, 200.0D, 200.0D, 0.5D);
        horizontalScale *= 2.0D;
        _selectorNoiseBuffer = _selectorNoise.Create(_selectorNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale / 80.0D, verticalScale / 160.0D, horizontalScale / 80.0D);
        _minLimitPerlinNoiseBuffer = _minLimitPerlinNoise.Create(_minLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        _maxLimitPerlinNoiseBuffer = _maxLimitPerlinNoise.Create(_maxLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        var xyzIndex = 0;
        var xzIndex = 0;

        for (var iX = 0; iX < sizeX; ++iX)
        {
            for (var iZ = 0; iZ < sizeZ; ++iZ)
            {
                ++xzIndex;

                for (var iY = 0; iY < sizeY; ++iY)
                {
                    double terrainDensity;
                    var lowNoiseSample = _minLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    var highNoiseSample = _maxLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    var selectorNoiseSample = (_selectorNoiseBuffer[xyzIndex] / 10.0D + 1.0D) / 2.0D;
                    if (selectorNoiseSample < 0.0D)
                    {
                        terrainDensity = lowNoiseSample;
                    }
                    else if (selectorNoiseSample > 1.0D)
                    {
                        terrainDensity = highNoiseSample;
                    }
                    else
                    {
                        terrainDensity = lowNoiseSample + (highNoiseSample - lowNoiseSample) * selectorNoiseSample;
                    }

                    terrainDensity -= 8.0D;
                    byte yMaxFade = 32;
                    double fadeout;
                    if (iY > sizeY - yMaxFade)
                    {
                        fadeout = (iY - (sizeY - yMaxFade)) / (yMaxFade - 1.0F);
                        terrainDensity = terrainDensity * (1.0D - fadeout) + -30.0D * fadeout;
                    }

                    yMaxFade = 8;
                    if (iY < yMaxFade)
                    {
                        fadeout = (yMaxFade - iY) / (yMaxFade - 1.0F);
                        terrainDensity = terrainDensity * (1.0D - fadeout) + -30.0D * fadeout;
                    }

                    heightMap[xyzIndex] = terrainDensity;
                    ++xyzIndex;
                }
            }
        }

        return heightMap;
    }

    // Deliberately separate from Overworld's palette: the two providers may evolve
    // independently, while each worker still shares one immutable resolved dependency set.
    internal sealed class BlockIds
    {
        private BlockIds(IBlockRuntimeView blocks, IReadOnlyDictionary<string, string>? references, ResourceLocation? owner)
        {
            Stone = Resolve("stone");
            Water = Resolve("water");
            FlowingWater = Resolve("flowing_water");
            Lava = Resolve("lava");
            FlowingLava = Resolve("flowing_lava");
            Dirt = Resolve("dirt");
            Gravel = Resolve("gravel");
            Sand = Resolve("sand");
            Sandstone = Resolve("sandstone");
            CoalOre = Resolve("coal_ore");
            IronOre = Resolve("iron_ore");
            GoldOre = Resolve("gold_ore");
            RedstoneOre = Resolve("redstone_ore");
            DiamondOre = Resolve("diamond_ore");
            LapisOre = Resolve("lapis_ore");
            Dandelion = Resolve("dandelion");
            Rose = Resolve("rose");
            BrownMushroom = Resolve("brown_mushroom");
            RedMushroom = Resolve("red_mushroom");
            Snow = Resolve("snow");

            int Resolve(string role)
            {
                var reference = references is null
                    ? $"omniblock:{role}"
                    : references.TryGetValue(role, out var configured)
                        ? configured
                        : throw new InvalidOperationException(
                            $"World type '{owner}' sky generator is missing block role '{role}'.");
                try
                {
                    return blocks.Get(ResourceLocation.Parse(reference)).Id;
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(
                        $"World type '{owner}' sky generator block role '{role}' references unknown block '{reference}'.",
                        error);
                }
            }
        }

        public int Stone { get; }
        public int Water { get; }
        public int FlowingWater { get; }
        public int Lava { get; }
        public int FlowingLava { get; }
        public int Dirt { get; }
        public int Gravel { get; }
        public int Sand { get; }
        public int Sandstone { get; }
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
        public int Snow { get; }

        public static BlockIds Resolve(
            IBlockRuntimeView blocks,
            IReadOnlyDictionary<string, string>? references = null,
            ResourceLocation? owner = null) => new(blocks, references, owner);
    }
}
