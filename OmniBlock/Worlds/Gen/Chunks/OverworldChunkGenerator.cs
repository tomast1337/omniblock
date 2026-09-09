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

internal class OverworldChunkGenerator : CommonChunkGenerator, IChunkSource
{
    private readonly BlockIds _blocks;
    private readonly Settings _settings;
    private readonly BiomeSource _biomeSource;
    private readonly Carver _carver = new CaveCarver();
    private readonly OctavePerlinNoiseSampler _depthNoise;
    private readonly OctavePerlinNoiseSampler _floatingIslandNoise;
    private readonly OctavePerlinNoiseSampler _floatingIslandScale;
    private readonly OctavePerlinNoiseSampler _forestNoise;
    private readonly OctavePerlinNoiseSampler _maxLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _minLimitPerlinNoise;
    private readonly OctavePerlinNoiseSampler _sandGravelNoise;

    // Seed and per-instance biome source (allows thread-safe parallel generation)
    private readonly OctavePerlinNoiseSampler _selectorNoise;
    private Biome[] _biomes;
    private double[] _depthBuffer = new double[256];
    private double[] _depthNoiseBuffer;
    private PlantPatchFeature _featureBrownMushroom;
    private CactusPatchFeature _featureCactus;
    private ClayOreFeature _featureClay;
    private OreFeature _featureCoal;
    private PlantPatchFeature _featureDandelion;
    private DeadBushPatchFeature _featureDeadBush;
    private OreFeature _featureDiamond;
    private OreFeature _featureDirt;
    private DungeonFeature _featureDungeon;
    private OreFeature _featureGold;
    private GrassPatchFeature _featureGrass1;
    private GrassPatchFeature _featureGrass2;
    private OreFeature _featureGravel;
    private OreFeature _featureIron;
    private OreFeature _featureLapis;
    private LakeFeature _featureLavaLake;
    private SpringFeature _featureLavaSpring;
    private PumpkinPatchFeature _featurePumpkin;
    private PlantPatchFeature _featureRedMushroom;
    private OreFeature _featureRedstone;
    private PlantPatchFeature _featureRose;
    private SugarCanePatchFeature _featureSugarcane;

    // Pre-allocated feature instances reused across every decorated chunk
    private LakeFeature _featureWaterLake;
    private SpringFeature _featureWaterSpring;
    private double[] _gravelBuffer = new double[256];
    private double[] _heightMap;
    private double[] _maxLimitPerlinNoiseBuffer;
    private double[] _minLimitPerlinNoiseBuffer;
    private double[] _sandBuffer = new double[256];
    private double[] _scaleNoiseBuffer;
    private double[] _selectorNoiseBuffer;
    private double[] _temperatures;

    public OverworldChunkGenerator(IWorldContext world, long seed)
        : this(world, seed, world.Dimension.BiomeSource, BlockIds.Resolve(world.Content.Blocks), Settings.Default)
    {
    }

    internal OverworldChunkGenerator(IWorldContext world, long seed, BlockIds blocks, Settings settings)
        : this(world, seed, world.Dimension.BiomeSource, blocks, settings)
    {
    }

    private OverworldChunkGenerator(
        IWorldContext world,
        long seed,
        BiomeSource biomeSource,
        BlockIds blocks,
        Settings settings) : base(world, seed)
    {
        _blocks = blocks;
        _settings = settings;
        _minLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, settings.MinLimitOctaves);
        _maxLimitPerlinNoise = new OctavePerlinNoiseSampler(_random, settings.MaxLimitOctaves);
        _selectorNoise = new OctavePerlinNoiseSampler(_random, settings.SelectorOctaves);
        _sandGravelNoise = new OctavePerlinNoiseSampler(_random, settings.SurfaceOctaves);
        _depthNoise = new OctavePerlinNoiseSampler(_random, settings.DepthOctaves);
        _floatingIslandScale = new OctavePerlinNoiseSampler(_random, settings.FloatingScaleOctaves);
        _floatingIslandNoise = new OctavePerlinNoiseSampler(_random, settings.FloatingNoiseOctaves);
        _forestNoise = new OctavePerlinNoiseSampler(_random, settings.ForestOctaves);
        _biomeSource = biomeSource;
        InitFeatures();
    }

    // Creates a thread-safe parallel generator with its own BiomeSource and _random state.
    // All noise samplers are deterministically equivalent (same seed), so chunk output is identical.
    public IChunkSource CreateParallelInstance()
        => new OverworldChunkGenerator(_world, _seed, new BiomeSource(_world), _blocks, _settings);

    public Chunk LoadChunk(int chunkX, int chunkZ) => GetChunk(chunkX, chunkZ);

    internal sealed record Settings(
        int MinLimitOctaves,
        int MaxLimitOctaves,
        int SelectorOctaves,
        int SurfaceOctaves,
        int DepthOctaves,
        int FloatingScaleOctaves,
        int FloatingNoiseOctaves,
        int ForestOctaves,
        double HorizontalNoiseScale,
        double VerticalNoiseScale,
        int DungeonAttempts,
        int ClayAttempts,
        int DirtAttempts,
        int GravelAttempts,
        int CoalAttempts,
        int IronAttempts,
        int SurfaceLevel,
        double SurfaceNoiseScale,
        int BedrockDepth,
        int SandstoneDepthBound,
        FeatureSettings Features)
    {
        public static Settings Default { get; } = new(
            16, 16, 8, 4, 4, 10, 16, 8, 684.412D, 684.412D, 8, 10, 20, 10, 20, 20,
            64, 1.0D / 32.0D, 5, 4,
            FeatureSettings.Default);

        public void Validate(ResourceLocation owner)
        {
            Positive(nameof(MinLimitOctaves), MinLimitOctaves);
            Positive(nameof(MaxLimitOctaves), MaxLimitOctaves);
            Positive(nameof(SelectorOctaves), SelectorOctaves);
            Positive(nameof(SurfaceOctaves), SurfaceOctaves);
            Positive(nameof(DepthOctaves), DepthOctaves);
            Positive(nameof(FloatingScaleOctaves), FloatingScaleOctaves);
            Positive(nameof(FloatingNoiseOctaves), FloatingNoiseOctaves);
            Positive(nameof(ForestOctaves), ForestOctaves);
            Positive(nameof(HorizontalNoiseScale), HorizontalNoiseScale);
            Positive(nameof(VerticalNoiseScale), VerticalNoiseScale);
            NonNegative(nameof(DungeonAttempts), DungeonAttempts);
            NonNegative(nameof(ClayAttempts), ClayAttempts);
            NonNegative(nameof(DirtAttempts), DirtAttempts);
            NonNegative(nameof(GravelAttempts), GravelAttempts);
            NonNegative(nameof(CoalAttempts), CoalAttempts);
            NonNegative(nameof(IronAttempts), IronAttempts);
            Positive(nameof(SurfaceLevel), SurfaceLevel);
            Positive(nameof(SurfaceNoiseScale), SurfaceNoiseScale);
            Positive(nameof(BedrockDepth), BedrockDepth);
            Positive(nameof(SandstoneDepthBound), SandstoneDepthBound);
            Features.Validate(owner);

            void Positive(string name, double value)
            {
                if (!double.IsFinite(value) || value <= 0) Invalid(name, value, "must be finite and greater than zero");
            }

            void NonNegative(string name, int value)
            {
                if (value < 0) Invalid(name, value, "must not be negative");
            }

            void Invalid(string name, object value, string requirement) =>
                throw new InvalidOperationException(
                    $"World type '{owner}' overworld setting '{name}' is {value} and {requirement}.");
        }
    }

    internal sealed record FeatureSettings(
        int WaterLakeChance,
        int LavaLakeChance,
        int LavaLakeUpperY,
        int LavaLakeYOffset,
        int LavaLakeSurfaceY,
        int LavaLakeAboveSurfaceChance,
        int GoldAttempts,
        int GoldMaxY,
        int RedstoneAttempts,
        int RedstoneMaxY,
        int DiamondAttempts,
        int DiamondMaxY,
        int LapisAttempts,
        int LapisMaxY,
        int TreeBonusChance,
        int RoseChance,
        int BrownMushroomChance,
        int RedMushroomChance,
        int SugarcaneAttempts,
        int PumpkinChance,
        int WaterSpringAttempts,
        int WaterSpringUpperY,
        int WaterSpringYOffset,
        int LavaSpringAttempts,
        int LavaSpringUpperY,
        int LavaSpringYOffset,
        int ClayVeinSize,
        int DirtVeinSize,
        int GravelVeinSize,
        int CoalVeinSize,
        int IronVeinSize,
        int GoldVeinSize,
        int RedstoneVeinSize,
        int DiamondVeinSize,
        int LapisVeinSize)
    {
        public static FeatureSettings Default { get; } = new(
            4, 8, 120, 8, 64, 10, 2, 32, 8, 16, 1, 16, 1, 16, 10, 2, 4, 8,
            10, 32, 50, 120, 8, 20, 112, 8, 32, 32, 32, 16, 8, 8, 7, 7, 6);

        public void Validate(ResourceLocation owner)
        {
            foreach (var (name, value) in Values())
                if (name.EndsWith("Attempts", StringComparison.Ordinal) ? value < 0 : value <= 0)
                    throw new InvalidOperationException(
                        $"World type '{owner}' overworld feature setting '{name}' has invalid value {value}.");

            IEnumerable<(string, int)> Values()
            {
                yield return (nameof(WaterLakeChance), WaterLakeChance);
                yield return (nameof(LavaLakeChance), LavaLakeChance);
                yield return (nameof(LavaLakeUpperY), LavaLakeUpperY);
                yield return (nameof(LavaLakeYOffset), LavaLakeYOffset);
                yield return (nameof(LavaLakeSurfaceY), LavaLakeSurfaceY);
                yield return (nameof(LavaLakeAboveSurfaceChance), LavaLakeAboveSurfaceChance);
                yield return (nameof(GoldAttempts), GoldAttempts);
                yield return (nameof(GoldMaxY), GoldMaxY);
                yield return (nameof(RedstoneAttempts), RedstoneAttempts);
                yield return (nameof(RedstoneMaxY), RedstoneMaxY);
                yield return (nameof(DiamondAttempts), DiamondAttempts);
                yield return (nameof(DiamondMaxY), DiamondMaxY);
                yield return (nameof(LapisAttempts), LapisAttempts);
                yield return (nameof(LapisMaxY), LapisMaxY);
                yield return (nameof(TreeBonusChance), TreeBonusChance);
                yield return (nameof(RoseChance), RoseChance);
                yield return (nameof(BrownMushroomChance), BrownMushroomChance);
                yield return (nameof(RedMushroomChance), RedMushroomChance);
                yield return (nameof(SugarcaneAttempts), SugarcaneAttempts);
                yield return (nameof(PumpkinChance), PumpkinChance);
                yield return (nameof(WaterSpringAttempts), WaterSpringAttempts);
                yield return (nameof(WaterSpringUpperY), WaterSpringUpperY);
                yield return (nameof(WaterSpringYOffset), WaterSpringYOffset);
                yield return (nameof(LavaSpringAttempts), LavaSpringAttempts);
                yield return (nameof(LavaSpringUpperY), LavaSpringUpperY);
                yield return (nameof(LavaSpringYOffset), LavaSpringYOffset);
                yield return (nameof(ClayVeinSize), ClayVeinSize);
                yield return (nameof(DirtVeinSize), DirtVeinSize);
                yield return (nameof(GravelVeinSize), GravelVeinSize);
                yield return (nameof(CoalVeinSize), CoalVeinSize);
                yield return (nameof(IronVeinSize), IronVeinSize);
                yield return (nameof(GoldVeinSize), GoldVeinSize);
                yield return (nameof(RedstoneVeinSize), RedstoneVeinSize);
                yield return (nameof(DiamondVeinSize), DiamondVeinSize);
                yield return (nameof(LapisVeinSize), LapisVeinSize);
            }
        }
    }

    /// <summary>
    ///     Generates a chunk at the given coordinates. The chunk is generated by first creating a low-resolution height map,
    ///     then interpolating it to determine the base terrain, and finally carving caves and adding features to it.
    /// </summary>
    /// <param name="chunkX">The x-coordinate of the chunk</param>
    /// <param name="chunkZ">The z-coordinate of the chunk</param>
    /// <returns>The generated chunk</returns>
    public Chunk GetChunk(int chunkX, int chunkZ)
    {
        _random.SetSeed(chunkX * 341873128712L + chunkZ * 132897987541L);
        var blocks = new byte[ChuckFormat.ChunkSize];
        Chunk chunk = new(_world, blocks, chunkX, chunkZ);
        _biomes = _biomeSource.GetBiomesInArea(_biomes, chunkX * 16, chunkZ * 16, 16, 16);
        var temperatureMap = _biomeSource.TemperatureMap;
        BuildTerrain(chunkX, chunkZ, blocks, _biomes, temperatureMap);
        BuildSurfaces(chunkX, chunkZ, blocks, _biomes);
        _carver.carve(this, _world, chunkX, chunkZ, blocks);
        chunk.PopulateHeightMap();
        return chunk;
    }

    public bool IsChunkLoaded(int x, int z) => true;

    /// <summary>
    ///     Generates the features of the chunk, such as ores, trees, lakes, etc. The features that are generated depend on the
    ///     biome of the chunk and some _random factors.
    /// </summary>
    /// <param name="source">The chunk source that is generating the chunk</param>
    /// <param name="chunkX">The x-coordinate of the chunk</param>
    /// <param name="chunkZ">The z-coordinate of the chunk</param>
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

        // Generate lakes
        if (_random.NextInt(_settings.Features.WaterLakeChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureWaterLake.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate lava lakes
        if (_random.NextInt(_settings.Features.LavaLakeChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(
                _random.NextInt(_settings.Features.LavaLakeUpperY) + _settings.Features.LavaLakeYOffset);
            featureZ = blockZ + _random.NextInt(16) + 8;
            if (featureY < _settings.Features.LavaLakeSurfaceY
                || _random.NextInt(_settings.Features.LavaLakeAboveSurfaceChance) == 0)
            {
                _featureLavaLake.Generate(_world, _random, featureX, featureY, featureZ);
            }
        }

        // Generate Dungeons
        for (var i = 0; i < _settings.DungeonAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureDungeon.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Clay patches
        for (var i = 0; i < _settings.ClayAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16);
            _featureClay.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Dirt blobs
        for (var i = 0; i < _settings.DirtAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16);
            _featureDirt.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Gravel blobs
        for (var i = 0; i < _settings.GravelAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16);
            _featureGravel.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Coal Ore Veins
        for (var i = 0; i < _settings.CoalAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16);
            _featureCoal.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Iron Ore Veins
        for (var i = 0; i < _settings.IronAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(64);
            featureZ = blockZ + _random.NextInt(16);
            _featureIron.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Gold Ore Veins
        for (var i = 0; i < _settings.Features.GoldAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(_settings.Features.GoldMaxY);
            featureZ = blockZ + _random.NextInt(16);
            _featureGold.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Redstone Ore Veins
        for (var i = 0; i < _settings.Features.RedstoneAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(_settings.Features.RedstoneMaxY);
            featureZ = blockZ + _random.NextInt(16);
            _featureRedstone.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Diamond Ore Veins
        for (var i = 0; i < _settings.Features.DiamondAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(_settings.Features.DiamondMaxY);
            featureZ = blockZ + _random.NextInt(16);
            _featureDiamond.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Lapis Lazuli Ore Veins
        for (var i = 0; i < _settings.Features.LapisAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16);
            featureY = _random.NextInt(_settings.Features.LapisMaxY);
            featureZ = blockZ + _random.NextInt(16);
            _featureLapis.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Determine the number and type of trees that should be generated
        fraction = 0.5D;
        var treeDensitySample = (int)((_forestNoise.GenerateNoise(blockX * fraction, blockZ * fraction) / 8.0D + _random.NextDouble() * 4.0D + 4.0D) / 3.0D);
        var numberOfTrees = 0;
        if (_random.NextInt(_settings.Features.TreeBonusChance) == 0)
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

        // Choose an appropriate amount of Dandelions
        byte amountOfDandelions = 0;
        if (chunkBiome == Biome.Forest)
        {
            amountOfDandelions = 2;
        }

        if (chunkBiome == Biome.SeasonalForest)
        {
            amountOfDandelions = 4;
        }

        if (chunkBiome == Biome.Taiga)
        {
            amountOfDandelions = 2;
        }

        if (chunkBiome == Biome.Plains)
        {
            amountOfDandelions = 3;
        }


        // Generate Dandelions
        for (byte i = 0; i < amountOfDandelions; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureDandelion.Generate(_world, _random, featureX, featureY, featureZ);
        }

        byte amountOfTallgrass = 0;
        if (chunkBiome == Biome.Forest)
        {
            amountOfTallgrass = 2;
        }

        if (chunkBiome == Biome.Rainforest)
        {
            amountOfTallgrass = 10;
        }

        if (chunkBiome == Biome.SeasonalForest)
        {
            amountOfTallgrass = 2;
        }

        if (chunkBiome == Biome.Taiga)
        {
            amountOfTallgrass = 1;
        }

        if (chunkBiome == Biome.Plains)
        {
            amountOfTallgrass = 10;
        }

        // Generate Tallgrass and Ferns
        for (byte i = 0; i < amountOfTallgrass; ++i)
        {
            byte grassMeta = 1;
            if (chunkBiome == Biome.Rainforest && _random.NextInt(3) != 0)
            {
                // Fern
                grassMeta = 2;
            }

            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            (grassMeta == 1 ? _featureGrass1 : _featureGrass2).Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Deadbushes
        byte amountOfDeadBushes = 0;
        if (chunkBiome == Biome.Desert)
        {
            amountOfDeadBushes = 2;
        }

        for (byte i = 0; i < amountOfDeadBushes; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureDeadBush.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Roses
        if (_random.NextInt(_settings.Features.RoseChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureRose.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Brown Mushrooms
        if (_random.NextInt(_settings.Features.BrownMushroomChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureBrownMushroom.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Red Mushrooms
        if (_random.NextInt(_settings.Features.RedMushroomChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureRedMushroom.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Sugarcane
        for (var i = 0; i < _settings.Features.SugarcaneAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureSugarcane.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Pumpkin Patches
        if (_random.NextInt(_settings.Features.PumpkinChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featurePumpkin.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate Cacti
        byte amountOfCacti = 0;
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

        // Generate one-block water sources
        for (var i = 0; i < _settings.Features.WaterSpringAttempts; ++i)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(
                _random.NextInt(_settings.Features.WaterSpringUpperY) + _settings.Features.WaterSpringYOffset);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureWaterSpring.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Generate one-block lava sources
        for (var x = 0; x < _settings.Features.LavaSpringAttempts; ++x)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(
                _random.NextInt(
                    _random.NextInt(_settings.Features.LavaSpringUpperY) + _settings.Features.LavaSpringYOffset)
                + _settings.Features.LavaSpringYOffset);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureLavaSpring.Generate(_world, _random, featureX, featureY, featureZ);
        }

        // Place Snow in cold regions
        _temperatures = _biomeSource.GetTemperatures(_temperatures, blockX + 8, blockZ + 8, 16, 16);

        for (var x = blockX + 8; x < blockX + 8 + 16; ++x)
        {
            for (var z = blockZ + 8; z < blockZ + 8 + 16; ++z)
            {
                var offsetX = x - (blockX + 8);
                var offsetZ = z - (blockZ + 8);
                var topSolidBlockY = _world.Reader.GetTopSolidBlockY(x, z);
                var temperatureSample = _temperatures[offsetX * 16 + offsetZ] - (topSolidBlockY - 64) / 64.0D * 0.3D;
                if (temperatureSample < 0.5D && topSolidBlockY > 0 && topSolidBlockY < ChuckFormat.WorldHeight && _world.Reader.IsAir(x, topSolidBlockY, z) && _world.Reader.GetMaterial(x, topSolidBlockY - 1, z).BlocksMovement &&
                    _world.Reader.GetMaterial(x, topSolidBlockY - 1, z) != Material.Ice)
                {
                    _world.Writer.SetBlock(x, topSolidBlockY, z, _blocks.Snow, 0, false);
                }
            }
        }

        FallingBlockBehavior.FallInstantly = false;
    }

    public bool Save(bool saveEntities, LoadingDisplay display) => true;

    public bool Tick() => false;

    public bool CanSave() => true;

    public string GetDebugInfo() => "RandomLevelSource";

    private void InitFeatures()
    {
        _featureWaterLake = new LakeFeature(_blocks.Water);
        _featureLavaLake = new LakeFeature(_blocks.Lava);
        _featureDungeon = new DungeonFeature();
        _featureClay = new ClayOreFeature(_settings.Features.ClayVeinSize);
        _featureDirt = new OreFeature(_blocks.Dirt, _settings.Features.DirtVeinSize);
        _featureGravel = new OreFeature(_blocks.Gravel, _settings.Features.GravelVeinSize);
        _featureCoal = new OreFeature(_blocks.CoalOre, _settings.Features.CoalVeinSize);
        _featureIron = new OreFeature(_blocks.IronOre, _settings.Features.IronVeinSize);
        _featureGold = new OreFeature(_blocks.GoldOre, _settings.Features.GoldVeinSize);
        _featureRedstone = new OreFeature(_blocks.RedstoneOre, _settings.Features.RedstoneVeinSize);
        _featureDiamond = new OreFeature(_blocks.DiamondOre, _settings.Features.DiamondVeinSize);
        _featureLapis = new OreFeature(_blocks.LapisOre, _settings.Features.LapisVeinSize);
        _featureDandelion = new PlantPatchFeature(_blocks.Dandelion);
        _featureGrass1 = new GrassPatchFeature(_blocks.Grass, 1);
        _featureGrass2 = new GrassPatchFeature(_blocks.Grass, 2);
        _featureDeadBush = new DeadBushPatchFeature(_blocks.DeadBush);
        _featureRose = new PlantPatchFeature(_blocks.Rose);
        _featureBrownMushroom = new PlantPatchFeature(_blocks.BrownMushroom);
        _featureRedMushroom = new PlantPatchFeature(_blocks.RedMushroom);
        _featureSugarcane = new SugarCanePatchFeature();
        _featurePumpkin = new PumpkinPatchFeature();
        _featureCactus = new CactusPatchFeature();
        _featureWaterSpring = new SpringFeature(_blocks.FlowingWater);
        _featureLavaSpring = new SpringFeature(_blocks.FlowingLava);
    }

    /// <summary>
    ///     Generate the base terrain
    /// </summary>
    /// <param name="chunkX">X-Coordinate of this chunk</param>
    /// <param name="chunkZ">Z-Coordinate of this chunk</param>
    /// <param name="blocks">1D Array of Blocks within this chunk</param>
    /// <param name="biomes">1D Array of Biome values within this chunk</param>
    /// <param name="temperatures">1D Array of Temperature values within this chunk</param>
    /// <returns>The interpolated result.</returns>
    public void BuildTerrain(int chunkX, int chunkZ, byte[] blocks, Biome[] biomes, double[] temperatures)
    {
        // TODO: Replace some of these with global-constants
        //const byte vertScale = 8; // ChunkHeight / 8 = 16 (?)
        const byte horiScale = 4; // ChunkWidth / 4 = 4
        const byte halfChunkHeight = 64;
        const int xMax = horiScale + 1; // ChunkWidth / 4 + 1
        const byte yMax = 17; // ChunkHeight / 8 + 1
        const int zMax = horiScale + 1; // ChunkWidth / 4 + 1

        // Generate 4x16x4 low resolution noise map
        _heightMap = GenerateHeightMap(_heightMap, chunkX * horiScale, 0, chunkZ * horiScale, xMax, yMax, zMax);

        // Terrain noise is trilinearly interpolated and only sampled every 4 blocks
        for (var sampleX = 0; sampleX < horiScale; ++sampleX)
        {
            for (var sampleZ = 0; sampleZ < horiScale; ++sampleZ)
            {
                // Chunk Height / 8 = 16
                for (var sampleY = 0; sampleY < 16; ++sampleY)
                {
                    const double verticalLerpStep = 0.125D;
                    var corner000 = _heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner010 = _heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner100 = _heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 0];
                    var corner110 = _heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 0];
                    var corner001 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner000) * verticalLerpStep;
                    var corner011 = (_heightMap[((sampleX + 0) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner010) * verticalLerpStep;
                    var corner101 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 0) * yMax + sampleY + 1] - corner100) * verticalLerpStep;
                    var corner111 = (_heightMap[((sampleX + 1) * zMax + sampleZ + 1) * yMax + sampleY + 1] - corner110) * verticalLerpStep;

                    // Interpolate the 1/4th scale noise
                    for (var subY = 0; subY < 8; ++subY)
                    {
                        const double horizontalLerpStep = 0.25D; // 1.0 / horiScale
                        var terrainX0 = corner000;
                        var terrainX1 = corner010;
                        var terrainStepX0 = (corner100 - corner000) * horizontalLerpStep;
                        var terrainStepX1 = (corner110 - corner010) * horizontalLerpStep;

                        for (var subX = 0; subX < 4; ++subX)
                        {
                            var blockIndex = ChuckFormat.GetIndex(subX + sampleX * 4, sampleY * 8 + subY, sampleZ * 4);

                            var terrainDensity = terrainX0;
                            var densityStepZ = (terrainX1 - terrainX0) * horizontalLerpStep;

                            for (var subZ = 0; subZ < 4; ++subZ)
                            {
                                // Here the actual block is determined
                                // Default to air block
                                var blockType = 0;

                                // If water is too cold, turn into ice
                                var temp = temperatures[(sampleX * 4 + subX) * 16 + sampleZ * 4 + subZ];
                                if (sampleY * 8 + subY < halfChunkHeight)
                                {
                                    if (temp < 0.5D && sampleY * 8 + subY >= halfChunkHeight - 1)
                                    {
                                        blockType = _blocks.Ice;
                                    }
                                    else
                                    {
                                        blockType = _blocks.Water;
                                    }
                                }

                                // If the terrain density is above 0.0,
                                // turn it into stone
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

    /// <summary>
    ///     Generate the base terrain
    /// </summary>
    /// <param name="chunkX">X-Coordinate of this chunk</param>
    /// <param name="chunkZ">Z-Coordinate of this chunk</param>
    /// <param name="blocks">1D Array of Blocks within this chunk</param>
    /// <param name="biomes">1D Array of Biome values within this chunk</param>
    /// <returns>The interpolated result.</returns>
    public void BuildSurfaces(int chunkX, int chunkZ, byte[] blocks, Biome[] biomes)
    {
        var waterLevel = _settings.SurfaceLevel;
        var oneThirtySecond = _settings.SurfaceNoiseScale;
        _sandBuffer = _sandGravelNoise.Create(_sandBuffer, chunkX * 16, chunkZ * 16, 0.0D, 16, 16, 1, oneThirtySecond, oneThirtySecond, 1.0D);
        _gravelBuffer = _sandGravelNoise.Create(_gravelBuffer, chunkX * 16, 109.0134D, chunkZ * 16, 16, 1, 16, oneThirtySecond, 1.0D, oneThirtySecond);
        _depthBuffer = _depthNoise.Create(_depthBuffer, chunkX * 16, chunkZ * 16, 0.0D, 16, 16, 1, oneThirtySecond * 2.0D, oneThirtySecond * 2.0D, oneThirtySecond * 2.0D);

        var maxHeight = Math.Min(ChuckFormat.WorldHeight, ChuckFormat.ChunkHeight) - 1;

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var localBiome = biomes[localX + localZ * 16];
                var sandActive = _sandBuffer[localX + localZ * 16] + _random.NextDouble() * 0.2D > 0.0D;
                var gravelActive = _gravelBuffer[localX + localZ * 16] + _random.NextDouble() * 0.2D > 3.0D;
                var surfaceDepth = (int)(_depthBuffer[localX + localZ * 16] / 3.0D + 3.0D + _random.NextDouble() * 0.25D);
                var currentDepth = -1;
                var topBlock = localBiome.TopBlockId;
                var soilBlock = localBiome.SoilBlockId;

                for (var blockY = maxHeight; blockY >= 0; --blockY)
                {
                    var blockIndex = (localZ * 16 + localX) * ChuckFormat.ChunkHeight + blockY;
                    // Generate Bedrock floor
                    if (blockY <= _random.NextInt(_settings.BedrockDepth))
                    {
                        blocks[blockIndex] = (byte)_blocks.Bedrock;
                    }
                    else
                    {
                        var activeBlock = blocks[blockIndex];
                        if (activeBlock == 0) // Air
                        {
                            currentDepth = -1;
                        }
                        else if (activeBlock == _blocks.Stone)
                        {
                            if (currentDepth == -1)
                            {
                                if (surfaceDepth <= 0)
                                {
                                    topBlock = 0;
                                    soilBlock = (byte)_blocks.Stone;
                                }
                                else if (blockY >= waterLevel - 4 && blockY <= waterLevel + 1)
                                {
                                    topBlock = localBiome.TopBlockId;
                                    soilBlock = localBiome.SoilBlockId;
                                    if (gravelActive)
                                    {
                                        topBlock = 0;
                                    }

                                    if (gravelActive)
                                    {
                                        soilBlock = (byte)_blocks.Gravel;
                                    }

                                    if (sandActive)
                                    {
                                        topBlock = (byte)_blocks.Sand;
                                    }

                                    if (sandActive)
                                    {
                                        soilBlock = (byte)_blocks.Sand;
                                    }
                                }

                                if (blockY < waterLevel && topBlock == 0)
                                {
                                    topBlock = (byte)_blocks.Water;
                                }

                                currentDepth = surfaceDepth;
                                if (blockY >= waterLevel - 1)
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
                                    currentDepth = _random.NextInt(_settings.SandstoneDepthBound);
                                    soilBlock = (byte)_blocks.Sandstone;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// @brief Generates the low-resolution height map that is used to generate the terrain of the overworld. The height map is generated by sampling 5 different noise maps and applying biome-dependent modifications to them.
    ///
    /// @param terrainMap The terrain map that the scaled-down terrain values will be written to
    /// @param chunkPos The x,y,z coordinate of the sub-chunk
    /// @param max Defines the area of the terrainMap
    /// <summary>
    ///     Generates the low-resolution height map that is used to generate the terrain of the overworld. The height map is
    ///     generated by sampling 5 different noise maps and applying biome-dependent modifications to them.
    /// </summary>
    /// <param name="heightMap">The terrain map that the scaled-down terrain values will be written to</param>
    /// <param name="x">The x-coordinate of the sub-chunk</param>
    /// <param name="y">The y-coordinate of the sub-chunk</param>
    /// <param name="z">The z-coordinate of the sub-chunk</param>
    /// <param name="sizeX">The x-size of the terrainMap</param>
    /// <param name="sizeY">The y-size of the terrainMap</param>
    /// <param name="sizeZ">The z-size of the terrainMap</param>
    /// <returns>The generated height map</returns>
    private double[] GenerateHeightMap(double[]? heightMap, int x, int y, int z, int sizeX, int sizeY, int sizeZ)
    {
        if (heightMap == null)
        {
            heightMap = new double[sizeX * sizeY * sizeZ];
        }

        var horizontalScale = _settings.HorizontalNoiseScale;
        var verticalScale = _settings.VerticalNoiseScale;
        var temperatureBuffer = _biomeSource.TemperatureMap;
        var downfallBuffer = _biomeSource.DownfallMap;
        _scaleNoiseBuffer = _floatingIslandScale.Create(_scaleNoiseBuffer, x, z, sizeX, sizeZ, 1.121D, 1.121D, 0.5D);
        _depthNoiseBuffer = _floatingIslandNoise.Create(_depthNoiseBuffer, x, z, sizeX, sizeZ, 200.0D, 200.0D, 0.5D);
        _selectorNoiseBuffer = _selectorNoise.Create(_selectorNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale / 80.0D, verticalScale / 160.0D, horizontalScale / 80.0D);
        _minLimitPerlinNoiseBuffer = _minLimitPerlinNoise.Create(_minLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        _maxLimitPerlinNoiseBuffer = _maxLimitPerlinNoise.Create(_maxLimitPerlinNoiseBuffer, x, y, z, sizeX, sizeY, sizeZ, horizontalScale, verticalScale, horizontalScale);
        // Used to iterate 3D noise maps (low, high, selector)
        var xyzIndex = 0;
        // Used to iterate 2D Noise maps (depth, continentalness)
        var xzIndex = 0;
        var scaleFraction = 16 / sizeX;

        for (var iX = 0; iX < sizeX; ++iX)
        {
            var sampleX = iX * scaleFraction + scaleFraction / 2;

            for (var iZ = 0; iZ < sizeZ; ++iZ)
            {
                // Sample 2D noises
                var sampleZ = iZ * scaleFraction + scaleFraction / 2;
                // Apply biome-noise-dependent variety
                var temperatureSample = temperatureBuffer[sampleX * 16 + sampleZ];
                var downfallSample = downfallBuffer[sampleX * 16 + sampleZ] * temperatureSample;
                downfallSample = 1.0D - downfallSample;
                downfallSample *= downfallSample;
                downfallSample *= downfallSample;
                downfallSample = 1.0D - downfallSample;
                // Sample scale/contientalness noise
                var scaleNoiseSample = (_scaleNoiseBuffer[xzIndex] + 256.0D) / 512.0D;
                scaleNoiseSample *= downfallSample;
                if (scaleNoiseSample > 1.0D)
                {
                    scaleNoiseSample = 1.0D;
                }

                // Sample depth noise
                var depthNoiseSample = _depthNoiseBuffer[xzIndex] / 8000.0D;
                if (depthNoiseSample < 0.0D)
                {
                    depthNoiseSample = -depthNoiseSample * 0.3D;
                }

                depthNoiseSample = depthNoiseSample * 3.0D - 2.0D;
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

                    depthNoiseSample /= 8.0D;
                }

                if (scaleNoiseSample < 0.0D)
                {
                    scaleNoiseSample = 0.0D;
                }

                scaleNoiseSample += 0.5D;
                depthNoiseSample = depthNoiseSample * sizeY / 16.0D;
                var elevationOffset = sizeY / 2.0D + depthNoiseSample * 4.0D;
                ++xzIndex;

                for (var iY = 0; iY < sizeY; ++iY)
                {
                    double terrainDensity;
                    var densityOffset = (iY - elevationOffset) * 12.0D / scaleNoiseSample;
                    if (densityOffset < 0.0D)
                    {
                        densityOffset *= 4.0D;
                    }

                    // Sample low noise
                    var lowNoiseSample = _minLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    // Sample high noise
                    var highNoiseSample = _maxLimitPerlinNoiseBuffer[xyzIndex] / 512.0D;
                    // Sample selector noise
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

                    terrainDensity -= densityOffset;
                    // Reduce density towards max height
                    if (iY > sizeY - 4)
                    {
                        double surfaceBlend = (iY - (sizeY - 4)) / 3.0F;
                        terrainDensity = terrainDensity * (1.0D - surfaceBlend) + -10.0D * surfaceBlend;
                    }

                    heightMap[xyzIndex] = terrainDensity;
                    ++xyzIndex;
                }
            }
        }

        return heightMap;
    }

    /// <summary>
    ///     Immutable references compiled once for the generator and shared with its workers.
    ///     Keeping names out of terrain/decorator loops also makes the eventual provider-owned
    ///     configuration boundary explicit without combining Overworld and Sky RNG programs.
    /// </summary>
    internal sealed class BlockIds
    {
        private BlockIds(IBlockRuntimeView blocks, IReadOnlyDictionary<string, string>? references, ResourceLocation? owner)
        {
            Stone = Resolve("stone");
            Water = Resolve("water");
            FlowingWater = Resolve("flowing_water");
            Lava = Resolve("lava");
            FlowingLava = Resolve("flowing_lava");
            Ice = Resolve("ice");
            Bedrock = Resolve("bedrock");
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
            Grass = Resolve("grass");
            DeadBush = Resolve("dead_bush");
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
                            $"World type '{owner}' overworld generator is missing block role '{role}'.");
                try
                {
                    return blocks.Get(ResourceLocation.Parse(reference)).Id;
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(
                        $"World type '{owner}' overworld generator block role '{role}' references unknown block '{reference}'.",
                        error);
                }
            }
        }

        public int Stone { get; }
        public int Water { get; }
        public int FlowingWater { get; }
        public int Lava { get; }
        public int FlowingLava { get; }
        public int Ice { get; }
        public int Bedrock { get; }
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
        public int Grass { get; }
        public int DeadBush { get; }
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
