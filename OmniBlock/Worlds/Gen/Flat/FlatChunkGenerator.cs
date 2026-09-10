using OmniBlock.Blocks;
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
    private readonly DungeonFeature _featureDungeon = new();
    private readonly PumpkinPatchFeature _featurePumpkin = new();
    private readonly SugarCanePatchFeature _featureSugarcane = new();
    private readonly FlatGeneratorInfo _generatorInfo;
    private readonly string _generatorOptions;
    private readonly JavaRandom _random;
    private readonly Settings _settings;
    private readonly IWorldContext _world;
    private PlantPatchFeature _featureBrownMushroom;
    private ClayOreFeature _featureClay;
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
        : this(world, generatorOptions, BlockIds.Resolve(world.Content.Blocks), Settings.Default)
    {
    }

    internal FlatChunkGenerator(
        IWorldContext world,
        string generatorOptions,
        BlockIds blocks,
        Settings settings)
    {
        _world = world;
        _blocks = blocks;
        _settings = settings;
        _generatorOptions = generatorOptions;
        _generatorInfo = FlatGeneratorInfo.CreateFromString(generatorOptions, world.Content.Blocks);
        _random = new JavaRandom(world.Seed);
        InitFeatures();
    }

    public IChunkSource CreateParallelInstance() =>
        new FlatChunkGenerator(_world, _generatorOptions, _blocks, _settings);

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

        if (hasLakes && _random.NextInt(_settings.Features.WaterLakeChance) == 0)
        {
            featureX = blockX + _random.NextInt(16) + 8;
            featureY = _random.NextInt(128);
            featureZ = blockZ + _random.NextInt(16) + 8;
            _featureWaterLake.Generate(_world, _random, featureX, featureY, featureZ);
        }

        if (hasLavaLakes && _random.NextInt(_settings.Features.LavaLakeChance) == 0)
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

        if (hasDungeons)
        {
            for (var i = 0; i < _settings.DungeonAttempts; ++i)
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
            for (var i = 0; i < _settings.ClayAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureClay.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.DirtAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureDirt.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.GravelAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureGravel.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.CoalAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16);
                _featureCoal.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.IronAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(64);
                featureZ = blockZ + _random.NextInt(16);
                _featureIron.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.GoldAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(_settings.Features.GoldMaxY);
                featureZ = blockZ + _random.NextInt(16);
                _featureGold.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.RedstoneAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(_settings.Features.RedstoneMaxY);
                featureZ = blockZ + _random.NextInt(16);
                _featureRedstone.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.DiamondAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(_settings.Features.DiamondMaxY);
                featureZ = blockZ + _random.NextInt(16);
                _featureDiamond.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.LapisAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16);
                featureY = _random.NextInt(_settings.Features.LapisMaxY)
                           + _random.NextInt(_settings.Features.LapisMaxY);
                featureZ = blockZ + _random.NextInt(16);
                _featureLapis.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Trees
            var numberOfTrees = 0;
            if (_random.NextInt(_settings.Features.TreeChance) == 0) numberOfTrees++;

            for (var i = 0; i < numberOfTrees; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureZ = blockZ + _random.NextInt(16) + 8;
                var treeFeature = chunkBiome.GetRandomWorldGenForTrees(_random);
                treeFeature.prepare(1.0D, 1.0D, 1.0D);
                treeFeature.Generate(_world, _random, featureX, _world.Reader.GetTopY(featureX, featureZ), featureZ);
            }

            // Flowers and Mushrooms
            for (var i = 0; i < _settings.Features.DandelionAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureDandelion.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(_settings.Features.RoseChance) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureRose.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(_settings.Features.BrownMushroomChance) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureBrownMushroom.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(_settings.Features.RedMushroomChance) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureRedMushroom.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Sugarcane, Pumpkins
            for (var i = 0; i < _settings.Features.SugarcaneAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureSugarcane.Generate(_world, _random, featureX, featureY, featureZ);
            }

            if (_random.NextInt(_settings.Features.PumpkinChance) == 0)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featurePumpkin.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Grass, Dead Bush, Cactus
            for (var i = 0; i < _settings.Features.GrassAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureGrass.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.DeadBushAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureDeadBush.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.CactusAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(128);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureCactus.Generate(_world, _random, featureX, featureY, featureZ);
            }

            // Spring Features
            for (var i = 0; i < _settings.Features.WaterSpringAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(
                    _random.NextInt(_settings.Features.WaterSpringUpperY) + _settings.Features.WaterSpringYOffset);
                featureZ = blockZ + _random.NextInt(16) + 8;
                _featureWaterSpring.Generate(_world, _random, featureX, featureY, featureZ);
            }

            for (var i = 0; i < _settings.Features.LavaSpringAttempts; ++i)
            {
                featureX = blockX + _random.NextInt(16) + 8;
                featureY = _random.NextInt(
                    _random.NextInt(
                        _random.NextInt(_settings.Features.LavaSpringUpperY) + _settings.Features.LavaSpringYOffset)
                    + _settings.Features.LavaSpringYOffset);
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
        _featureRose = new PlantPatchFeature(_blocks.Rose);
        _featureBrownMushroom = new PlantPatchFeature(_blocks.BrownMushroom);
        _featureRedMushroom = new PlantPatchFeature(_blocks.RedMushroom);
        _featureDeadBush = new DeadBushPatchFeature(_blocks.DeadBush);
        _featureGrass = new GrassPatchFeature(_blocks.Grass, 1);
        _featureWaterSpring = new SpringFeature(_blocks.FlowingWater);
        _featureLavaSpring = new SpringFeature(_blocks.FlowingLava);
    }

    internal sealed record Settings(
        int DungeonAttempts,
        int ClayAttempts,
        int DirtAttempts,
        int GravelAttempts,
        int CoalAttempts,
        int IronAttempts,
        FeatureSettings Features)
    {
        public static Settings Default { get; } = new(8, 10, 20, 10, 20, 20, FeatureSettings.Default);

        public void Validate(ResourceLocation owner)
        {
            NonNegative(nameof(DungeonAttempts), DungeonAttempts);
            NonNegative(nameof(ClayAttempts), ClayAttempts);
            NonNegative(nameof(DirtAttempts), DirtAttempts);
            NonNegative(nameof(GravelAttempts), GravelAttempts);
            NonNegative(nameof(CoalAttempts), CoalAttempts);
            NonNegative(nameof(IronAttempts), IronAttempts);
            Features.Validate(owner);

            void NonNegative(string name, int value)
            {
                if (value < 0)
                {
                    throw new InvalidOperationException(
                        $"World type '{owner}' flat setting '{name}' must not be negative (was {value}).");
                }
            }
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
        int TreeChance,
        int DandelionAttempts,
        int RoseChance,
        int BrownMushroomChance,
        int RedMushroomChance,
        int SugarcaneAttempts,
        int PumpkinChance,
        int GrassAttempts,
        int DeadBushAttempts,
        int CactusAttempts,
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
            4, 8, 120, 8, 64, 10, 2, 32, 8, 16, 1, 16, 1, 16, 10, 2, 2, 4, 8,
            10, 32, 20, 2, 10, 50, 120, 8, 20, 112, 8, 32, 32, 32, 16, 8, 8, 7, 7, 6);

        public void Validate(ResourceLocation owner)
        {
            foreach (var property in GetType().GetProperties())
            {
                var allowsZero = property.Name.EndsWith("Attempts", StringComparison.Ordinal);
                if (property.GetValue(this) is int value && (allowsZero ? value < 0 : value <= 0))
                {
                    throw new InvalidOperationException(
                        $"World type '{owner}' flat feature setting '{property.Name}' has invalid value {value}.");
                }
            }
        }
    }

    internal sealed class BlockIds
    {
        private BlockIds(IBlockRuntimeView blocks, IReadOnlyDictionary<string, string>? references, ResourceLocation? owner)
        {
            Water = Resolve("water");
            FlowingWater = Resolve("flowing_water");
            Lava = Resolve("lava");
            FlowingLava = Resolve("flowing_lava");
            Dirt = Resolve("dirt");
            Gravel = Resolve("gravel");
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
            DeadBush = Resolve("dead_bush");
            Grass = Resolve("grass");

            int Resolve(string role)
            {
                var reference = references is null
                    ? $"omniblock:{role}"
                    : references.TryGetValue(role, out var configured)
                        ? configured
                        : throw new InvalidOperationException(
                            $"World type '{owner}' flat generator is missing block role '{role}'.");
                try
                {
                    return blocks.Get(ResourceLocation.Parse(reference)).Id;
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(
                        $"World type '{owner}' flat generator block role '{role}' references unknown block '{reference}'.",
                        error);
                }
            }
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

        public static BlockIds Resolve(
            IBlockRuntimeView blocks,
            IReadOnlyDictionary<string, string>? references = null,
            ResourceLocation? owner = null) => new(blocks, references, owner);
    }
}
