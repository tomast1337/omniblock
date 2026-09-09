using OmniBlock.Blocks.Behaviors;
using OmniBlock.Util.Maths.Noise;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Generation.Generators.Carvers;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Gen.Chunks;

internal class NetherChunkGenerator : CommonChunkGenerator, IChunkSource
{
    private readonly BlockIds _blocks;
    private readonly Carver _cave = new NetherCaveCarver();
    private readonly OctavePerlinNoiseSampler _depthNoise;
    private readonly OctavePerlinNoiseSampler _maxLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _minLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _perlinNoise1;
    private readonly OctavePerlinNoiseSampler _perlinNoise2;
    private readonly OctavePerlinNoiseSampler _perlinNoise3;
    private readonly OctavePerlinNoiseSampler _scaleNoise;
    private double[] _depthBuffer = new double[256];
    private double[] _depthNoiseBuffer;
    private PlantPatchFeature _featureBrownMushroom;
    private GlowstoneClusterFeature _featureGlowstoneFull;
    private GlowstoneClusterFeatureRare _featureGlowstoneRare;
    private NetherFirePatchFeature _featureNetherFire;
    private NetherLavaSpringFeature _featureNetherLavaSpring;
    private PlantPatchFeature _featureRedMushroom;
    private double[] _gravelBuffer = new double[256];
    private double[] _heightMap;
    private double[] _maxLimitPerlinNoiseBuffer;
    private double[] _minLimitPerlinNoiseBuffer;
    private double[] _perlinNoiseBuffer;
    private double[] _sandBuffer = new double[256];
    private double[] _scaleNoiseBuffer;

    public NetherChunkGenerator(IWorldContext world, long seed)
        : this(world, seed, BlockIds.Resolve(world))
    {
    }

    private NetherChunkGenerator(IWorldContext world, long seed, BlockIds blocks) : base(world, seed)
    {
        _blocks = blocks;
        _minLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, 16);
        _maxLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, 16);
        _perlinNoise1 = new OctavePerlinNoiseSampler(_random, 8);
        _perlinNoise2 = new OctavePerlinNoiseSampler(_random, 4);
        _perlinNoise3 = new OctavePerlinNoiseSampler(_random, 4);
        _scaleNoise = new OctavePerlinNoiseSampler(_random, 10);
        _depthNoise = new OctavePerlinNoiseSampler(_random, 16);
        InitFeatures();
    }

    public IChunkSource CreateParallelInstance() => new NetherChunkGenerator(_world, _seed, _blocks);

    public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

    public Chunk GetChunk(int chunkX, int chunkZ)
    {
        _random.SetSeed(chunkX * 341873128712L + chunkZ * 132897987541L);
        var blocks = new byte[ChuckFormat.ChunkSize];
        BuildTerrain(chunkX, chunkZ, blocks);
        BuildSurfaces(chunkX, chunkZ, blocks);
        _cave.carve(this, _world, chunkX, chunkZ, blocks);
        Chunk chunk = new(_world, blocks, chunkX, chunkZ);
        return chunk;
    }

    public bool IsChunkLoaded(int x, int z) => true;

    public void DecorateTerrain(IChunkSource source, int x, int z)
    {
        FallingBlockBehavior.FallInstantly = true;
        var blockX = x * 16;
        var blockZ = z * 16;

        int numIterations;
        int featureX;
        int featureY;
        int featureZ;
        for (numIterations = 0; numIterations < 8; ++numIterations)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(120) + 4;
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureNetherLavaSpring.Generate(_world, _random, featureX, featureY, featureZ);
        }

        numIterations = _random.NextInt(_random.NextInt(10) + 1) + 1;

        int featureZFallback;
        for (featureX = 0; featureX < numIterations; ++featureX)
        {
            featureY = blockX + _random.NextInt(16) + 8;
            featureZ = _random.NextInt(120) + 4;
            featureZFallback = blockZ + _random.NextInt(16) + 8;
            _featureNetherFire.Generate(_world, _random, featureY, featureZ, featureZFallback);
        }

        numIterations = _random.NextInt(_random.NextInt(10) + 1);

        for (featureX = 0; featureX < numIterations; ++featureX)
        {
            featureY = blockX + _random.NextInt(16) + 8;
            featureZ = _random.NextInt(120) + 4;
            featureZFallback = blockZ + _random.NextInt(16) + 8;
            _featureGlowstoneFull.Generate(_world, _random, featureY, featureZ, featureZFallback);
        }

        for (featureX = 0; featureX < 10; ++featureX)
        {
            featureY = blockX + _random.NextInt(16) + 8;
            featureZ = _random.NextInt(128);
            featureZFallback = blockZ + _random.NextInt(16) + 8;
            _featureGlowstoneRare.Generate(_world, _random, featureY, featureZ, featureZFallback);
        }

        if (_random.NextInt(1) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureBrownMushroom.Generate(_world, _random, featureX, featureY, featureZ);
        }

        if (_random.NextInt(1) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureRedMushroom.Generate(_world, _random, featureX, featureY, featureZ);
        }

        FallingBlockBehavior.FallInstantly = false;
    }

    public bool Save(bool bl, LoadingDisplay display) => true;
    public bool Tick() => false;
    public bool CanSave() => true;
    public string GetDebugInfo() => "HellRandomLevelSource";

    private void InitFeatures()
    {
        _featureNetherLavaSpring = new NetherLavaSpringFeature(_blocks.FlowingLava);
        _featureNetherFire = new NetherFirePatchFeature();
        _featureGlowstoneFull = new GlowstoneClusterFeature();
        _featureGlowstoneRare = new GlowstoneClusterFeatureRare();
        _featureBrownMushroom = new PlantPatchFeature(_blocks.BrownMushroom);
        _featureRedMushroom = new PlantPatchFeature(_blocks.RedMushroom);
    }

    public void BuildTerrain(int chunkX, int chunkZ, byte[] blocks)
    {
        byte horiScale = 4;
        byte lavaLevel = 32;
        var xMax = horiScale + 1;
        byte yMax = 17;
        var zMax = horiScale + 1;

        _heightMap = GenerateHeightMap(_heightMap, chunkX * horiScale, 0, chunkZ * horiScale, xMax, yMax, zMax);

        for (var sampleX = 0; sampleX < horiScale; ++sampleX)
        {
            for (var sampleZ = 0; sampleZ < horiScale; ++sampleZ)
            {
                for (var sampleY = 0; sampleY < 16; ++sampleY)
                {
                    var verticalLerpStep = 0.125D;
                    var corner000 = _heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner010 = _heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner100 = _heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner110 = _heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner001 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner000) * verticalLerpStep;
                    var corner011 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner010) * verticalLerpStep;
                    var corner101 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner100) * verticalLerpStep;
                    var corner111 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner110) * verticalLerpStep;

                    for (var subY = 0; subY < 8; ++subY)
                    {
                        var horizontalLerpStep = 0.25D;
                        var terrainX0 = corner000;
                        var terrainX1 = corner010;
                        var terrainStepX0 = (corner100 - corner000) * horizontalLerpStep;
                        var terrainStepX1 = (corner110 - corner010) * horizontalLerpStep;

                        for (var subX = 0; subX < 4; ++subX)
                        {
                            var blockIndex = ChuckFormat.GetIndex(subX + sampleX * 4, sampleY * 8 + subY, sampleZ * 4);
                            var horizontalLerpStepZ = 0.25D;
                            var terrainDensity = terrainX0;
                            var densityStepZ = (terrainX1 - terrainX0) * horizontalLerpStepZ;

                            for (var subZ = 0; subZ < 4; ++subZ)
                            {
                                var blockType = 0;
                                if (sampleY * 8 + subY < lavaLevel)
                                {
                                    blockType = _blocks.Lava;
                                }

                                if (terrainDensity > 0.0D)
                                {
                                    blockType = _blocks.Netherrack;
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

    public void BuildSurfaces(int chunkX, int chunkZ, byte[] blocks)
    {
        byte seaLevel = 64;
        var noiseScale = 1.0D / 32.0D;
        _sandBuffer = _perlinNoise2.Create(_sandBuffer, chunkX * 16, chunkZ * 16, 0.0D, 16, 16, 1, noiseScale, noiseScale, 1.0D);
        _gravelBuffer = _perlinNoise2.Create(_gravelBuffer, chunkX * 16, 109.0134D, chunkZ * 16, 16, 1, 16, noiseScale, 1.0D, noiseScale);
        _depthBuffer = _perlinNoise3.Create(_depthBuffer, chunkX * 16, chunkZ * 16, 0.0D, 16, 16, 1, noiseScale * 2.0D, noiseScale * 2.0D, noiseScale * 2.0D);

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var isSoulsand = _sandBuffer[localX + localZ * 16] + _random.NextDouble() * 0.2D > 0.0D;
                var isGravel = _gravelBuffer[localX + localZ * 16] + _random.NextDouble() * 0.2D > 0.0D;
                var surfaceDepth = (int)(_depthBuffer[localX + localZ * 16] / 3.0D + 3.0D + _random.NextDouble() * 0.25D);
                var currentDepth = -1;
                var topBlock = (byte)_blocks.Netherrack;
                var soilBlock = (byte)_blocks.Netherrack;

                for (var blockY = 127; blockY >= 0; --blockY)
                {
                    var blockIndex = (localZ * 16 + localX) * 128 + blockY;
                    if (blockY >= 127 - _random.NextInt(5))
                    {
                        blocks[blockIndex] = (byte)_blocks.Bedrock;
                    }
                    else if (blockY <= 0 + _random.NextInt(5))
                    {
                        blocks[blockIndex] = (byte)_blocks.Bedrock;
                    }
                    else
                    {
                        var currentBlock = blocks[blockIndex];
                        if (currentBlock == 0)
                        {
                            currentDepth = -1;
                        }
                        else if (currentBlock == _blocks.Netherrack)
                        {
                            if (currentDepth == -1)
                            {
                                if (surfaceDepth <= 0)
                                {
                                    topBlock = 0;
                                    soilBlock = (byte)_blocks.Netherrack;
                                }
                                else if (blockY >= seaLevel - 4 && blockY <= seaLevel + 1)
                                {
                                    topBlock = (byte)_blocks.Netherrack;
                                    soilBlock = (byte)_blocks.Netherrack;
                                    if (isGravel)
                                    {
                                        topBlock = (byte)_blocks.Gravel;
                                    }

                                    if (isGravel)
                                    {
                                        soilBlock = (byte)_blocks.Netherrack;
                                    }

                                    if (isSoulsand)
                                    {
                                        topBlock = (byte)_blocks.SoulSand;
                                    }

                                    if (isSoulsand)
                                    {
                                        soilBlock = (byte)_blocks.SoulSand;
                                    }
                                }

                                if (blockY < seaLevel && topBlock == 0)
                                {
                                    topBlock = (byte)_blocks.Lava;
                                }

                                currentDepth = surfaceDepth;
                                if (blockY >= seaLevel - 1)
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
                            }
                        }
                    }
                }
            }
        }
    }

    private double[] GenerateHeightMap(double[]? heightMap, int x, int y, int z, int sizeX, int sizeY, int sizeZ)
    {
        if (heightMap == null)
        {
            heightMap = new double[sizeX * sizeY * sizeZ];
        }

        var horizontalScale = 684.412D;
        var verticalScale = 2053.236D;
        _scaleNoiseBuffer = _scaleNoise.Create(_scaleNoiseBuffer, x, y, z, sizeX, 1, sizeZ, 1.0D, 0.0D, 1.0D);
        _depthNoiseBuffer = _depthNoise.Create(_depthNoiseBuffer, x, y, z, sizeX, 1, sizeZ, 100.0D, 0.0D, 100.0D);
        _perlinNoiseBuffer = _perlinNoise1.Create(_perlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale / 80.0D, verticalScale / 60.0D, horizontalScale / 80.0D);
        _minLimitPerlinNoiseBuffer = _minLimitPerlinNoise.Create(_minLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        _maxLimitPerlinNoiseBuffer = _maxLimitPerlinNoise.Create(_maxLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        var xyzIndex = 0;
        var xzIndex = 0;
        var heightModifiers = new double[sizeY];

        int iY;
        for (iY = 0; iY < sizeY; ++iY)
        {
            heightModifiers[iY] = Math.Cos(iY * Math.PI * 6.0D / sizeY) * 2.0D;
            double modifier = iY;
            if (iY > sizeY / 2)
            {
                modifier = sizeY - 1 - iY;
            }

            if (modifier < 4.0D)
            {
                modifier = 4.0D - modifier;
                heightModifiers[iY] -= modifier * modifier * modifier * 10.0D;
            }
        }

        for (var iX = 0; iX < sizeX; ++iX)
        {
            for (var iZ = 0; iZ < sizeZ; ++iZ)
            {
                var scaleNoiseSample = (_scaleNoiseBuffer[xzIndex] + 256.0D) / 512.0D;
                if (scaleNoiseSample > 1.0D)
                {
                    scaleNoiseSample = 1.0D;
                }

                var densityOffset = 0.0D;
                var depthNoiseSample = _depthNoiseBuffer[xzIndex] / 8000.0D;
                if (depthNoiseSample < 0.0D)
                {
                    depthNoiseSample = -depthNoiseSample;
                }

                depthNoiseSample = depthNoiseSample * 3.0D - 3.0D;
                if (depthNoiseSample < 0.0D)
                {
                    depthNoiseSample /= 2.0D;
                    if (depthNoiseSample < -1.0D)
                    {
                        depthNoiseSample = -1.0D;
                    }

                    depthNoiseSample /= 1.4D;
                    depthNoiseSample /= 2.0D;
                    scaleNoiseSample = 0.0D;
                }
                else
                {
                    if (depthNoiseSample > 1.0D)
                    {
                        depthNoiseSample = 1.0D;
                    }

                    depthNoiseSample /= 6.0D;
                }

                scaleNoiseSample += 0.5D;
                depthNoiseSample = depthNoiseSample * sizeY / 16.0D;
                ++xzIndex;

                for (iY = 0; iY < sizeY; ++iY)
                {
                    var terrainDensity = 0.0D;
                    var shapeModifier = heightModifiers[iY];
                    var lowNoiseSample = _minLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    var highNoiseSample = _maxLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    var selectorNoiseSample = (_perlinNoiseBuffer[xyzIndex] / 10.0D + 1.0D) / 2.0D;
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

                    terrainDensity -= shapeModifier;
                    double fadeout;
                    if (iY > sizeY - 4)
                    {
                        fadeout = (iY - (sizeY - 4)) / 3.0F;
                        terrainDensity = terrainDensity * (1.0D - fadeout) + -10.0D * fadeout;
                    }

                    if (iY < densityOffset)
                    {
                        fadeout = (densityOffset - iY) / 4.0D;
                        if (fadeout < 0.0D)
                        {
                            fadeout = 0.0D;
                        }

                        if (fadeout > 1.0D)
                        {
                            fadeout = 1.0D;
                        }

                        terrainDensity = terrainDensity * (1.0D - fadeout) + -10.0D * fadeout;
                    }

                    heightMap[xyzIndex] = terrainDensity;
                    ++xyzIndex;
                }
            }
        }

        return heightMap;
    }

    private sealed class BlockIds
    {
        private BlockIds(IWorldContext world)
        {
            var blocks = world.Content.Blocks;
            Netherrack = blocks.Get("netherrack").Id;
            Lava = blocks.Get("lava").Id;
            FlowingLava = blocks.Get("flowing_lava").Id;
            Bedrock = blocks.Get("bedrock").Id;
            Gravel = blocks.Get("gravel").Id;
            SoulSand = blocks.Get("soulsand").Id;
            BrownMushroom = blocks.Get("brown_mushroom").Id;
            RedMushroom = blocks.Get("red_mushroom").Id;
        }

        public int Netherrack { get; }
        public int Lava { get; }
        public int FlowingLava { get; }
        public int Bedrock { get; }
        public int Gravel { get; }
        public int SoulSand { get; }
        public int BrownMushroom { get; }
        public int RedMushroom { get; }

        public static BlockIds Resolve(IWorldContext world) => new(world);
    }

    public static void markChunksForUnload(int _)
    {
    }
}
