using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Generation.Generators.Features;

namespace OmniBlock.Worlds.Generation.Biomes;

public class Biome
{
    private static readonly IRegistry<Biome> s_registry = DefaultRegistries.Biomes;

    public static readonly Biome Rainforest = Register(0, "rainforest", new BiomeGenRainforest().SetColor(0x8FA360).SetName("Rainforest").SetFoliageColor(0x1FF458));
    public static readonly Biome Swampland = Register(1, "swampland", new BiomeGenSwamp().SetColor(0x7F9B20).SetName("Swampland").SetFoliageColor(0x8BAF48));
    public static readonly Biome SeasonalForest = Register(2, "seasonal_forest", new Biome().SetColor(0x9BE023).SetName("Seasonal Forest"));
    public static readonly Biome Forest = Register(3, "forest", new BiomeGenForest().SetColor(0x566210).SetName("Forest").SetFoliageColor(0x4EBA31));
    public static readonly Biome Savanna = Register(4, "savanna", new BiomeGenDesert().SetColor(0xD9E023).SetName("Savanna"));
    public static readonly Biome Shrubland = Register(5, "shrubland", new Biome().SetColor(0xA1AD20).SetName("Shrubland"));
    public static readonly Biome Taiga = Register(6, "taiga", new BiomeGenTaiga().SetColor(0x2EB153).SetName("Taiga").EnableSnow().SetFoliageColor(0x7BB731));
    public static readonly Biome Desert = Register(7, "desert", new BiomeGenDesert().SetColor(0xFA9418).SetName("Desert").DisableRain());
    public static readonly Biome Plains = Register(8, "plains", new BiomeGenDesert().SetColor(0xFFD910).SetName("Plains"));
    public static readonly Biome IceDesert = Register(9, "ice_desert", new BiomeGenDesert().SetColor(0xFFED93).SetName("Ice Desert").EnableSnow().DisableRain().SetFoliageColor(0xC4D339));
    public static readonly Biome Tundra = Register(10, "tundra", new Biome().SetColor(0x57EBF9).SetName("Tundra").EnableSnow().SetFoliageColor(0xC4D339));
    public static readonly Biome Hell = Register(11, "hell", new BiomeGenHell().SetColor(0xFF0000).SetName("Hell").DisableRain());
    public static readonly Biome Sky = Register(12, "sky", new BiomeGenSky().SetColor(0x8080FF).SetName("Sky").DisableRain());

    private static readonly Biome[] s_biomes = new Biome[4096];
    public byte SoilBlockId = (byte)BlockRegistry.Get("dirt").Id;
    public byte TopBlockId = (byte)BlockRegistry.Get("grass_block").Id;

    static Biome() => Init();

    protected Biome()
    {
    }

    public string Name { get; private set; } = "";
    public int GrassColor { get; private set; }
    public int FoliageColor { get; private set; } = 0x4EE031;
    protected WeightedRandomSelector<SpawnListEntry> MonsterList { get; } = new();
    protected WeightedRandomSelector<SpawnListEntry> CreatureList { get; } = new();
    protected WeightedRandomSelector<SpawnListEntry> WaterCreatureList { get; } = new();

    public bool HasSnow { get; private set; }
    public bool HasRain { get; private set; } = true;

    /// <summary>
    ///     Fills every registered biome's spawn lists from <c>assets/biome_spawn/*.json</c>. Runs
    ///     after both the biome and entity registries are populated, since each entry names an
    ///     entity type that must already exist.
    /// </summary>
    internal static void LoadSpawnLists(
        IEnumerable<BiomeSpawnDefinition> definitions,
        IEntityTypeBuildView entityTypes)
    {
        var byName = definitions.ToDictionary(d => d.Name);

        foreach (var key in s_registry.Keys)
        {
            var biome = s_registry.GetOrThrow(key);
            biome.MonsterList.Clear();
            biome.CreatureList.Clear();
            biome.WaterCreatureList.Clear();

            if (!byName.TryGetValue(key.Path, out var definition)) continue;

            Fill(biome.MonsterList, definition.Monsters, entityTypes);
            Fill(biome.CreatureList, definition.Creatures, entityTypes);
            Fill(biome.WaterCreatureList, definition.WaterCreatures, entityTypes);
        }
    }

    private static void Fill(
        WeightedRandomSelector<SpawnListEntry> list,
        BiomeSpawnEntry[] entries,
        IEntityTypeBuildView entityTypes)
    {
        foreach (var entry in entries)
        {
            var key = ResourceLocation.Parse(entry.Entity);
            if (!entityTypes.TryGet(key, out var type))
            {
                throw new ArgumentException($"Biome spawn list references unknown entity '{entry.Entity}'.");
            }

            list.Add(new SpawnListEntry(w => (EntityLiving)type.Create(w)), entry.Weight);
        }
    }

    private static Biome Register(int id, string name, Biome biome)
    {
        s_registry.Register(id, ResourceLocation.Parse(name), biome);
        return biome;
    }

    protected Biome DisableRain()
    {
        HasRain = false;
        return this;
    }

    protected Biome EnableSnow()
    {
        HasSnow = true;
        return this;
    }

    protected Biome SetName(string name)
    {
        Name = name;
        return this;
    }

    protected Biome SetFoliageColor(int color)
    {
        FoliageColor = color;
        return this;
    }

    protected Biome SetColor(int color)
    {
        GrassColor = color;
        return this;
    }

    public static void Init()
    {
        for (var i = 0; i < 64; ++i)
        {
            for (var j = 0; j < 64; ++j)
            {
                s_biomes[i + j * 64] = LocateBiome(i / 63.0F, j / 63.0F);
            }
        }

        Desert.TopBlockId = Desert.SoilBlockId = (byte)BlockRegistry.Get("sand").Id;
        IceDesert.TopBlockId = IceDesert.SoilBlockId = (byte)BlockRegistry.Get("sand").Id;
    }

    public virtual Feature GetRandomWorldGenForTrees(JavaRandom rand) => rand.NextInt(10) == 0 ? new LargeOakTreeFeature() : new OakTreeFeature();


    public static Biome GetBiome(double temp, double downfall)
    {
        var x = (int)(temp * 63.0D);
        var y = (int)(downfall * 63.0D);
        return s_biomes[x + y * 64];
    }

    public static Biome LocateBiome(float temperature, float downfall)
    {
        downfall *= temperature;
        if (temperature < 0.1f) return Tundra;
        if (downfall < 0.2f)
        {
            if (temperature < 0.5f) return Tundra;
            return temperature < 0.95f ? Savanna : Desert;
        }

        if (downfall > 0.5f && temperature < 0.7f) return Swampland;
        if (temperature < 0.5f) return Taiga;
        if (temperature < 0.97f) return downfall < 0.35f ? Shrubland : Forest;
        if (downfall < 0.45f) return Plains;
        return downfall < 0.9f ? SeasonalForest : Rainforest;
    }

    public virtual int GetSkyColorByTemp(float temperature)
    {
        temperature /= 3.0F;
        if (temperature < -1.0F)
        {
            temperature = -1.0F;
        }

        if (temperature > 1.0F)
        {
            temperature = 1.0F;
        }

        return ToRgb(224.0f / 360.0f - temperature * 0.05f, 0.5f + temperature * 0.1f, 1.0f);
    }

    public static (int R, int G, int B) FromHsbColor(float hue, float saturation, float brightness)
    {
        if (saturation == 0f)
        {
            var gray = (int)(brightness * 255f + 0.5f);
            return (gray, gray, gray);
        }

        var h = (hue - MathF.Floor(hue)) * 6f;
        var f = h - MathF.Floor(h);
        var p = brightness * (1f - saturation);
        var q = brightness * (1f - saturation * f);
        var t = brightness * (1f - saturation * (1f - f));

        return (int)h switch
        {
            0 => ToRgb(brightness, t, p),
            1 => ToRgb(q, brightness, p),
            2 => ToRgb(p, brightness, t),
            3 => ToRgb(p, q, brightness),
            4 => ToRgb(t, p, brightness),
            _ => ToRgb(brightness, p, q)
        };

        static (int, int, int) ToRgb(float r, float g, float b) =>
            ((int)(r * 255f + 0.5f), (int)(g * 255f + 0.5f), (int)(b * 255f + 0.5f));
    }

    public static int ToRgb(float hue, float saturation, float brightness)
    {
        var (r, g, b) = FromHsbColor(hue, saturation, brightness);
        return (255 << 24) | (r << 16) | (g << 8) | b;
    }

    public WeightedRandomSelector<SpawnListEntry> GetSpawnableList(CreatureKind kind)
    {
        if (kind == CreatureKind.Monster) return MonsterList;
        if (kind == CreatureKind.Creature) return CreatureList;
        if (kind == CreatureKind.WaterCreature) return WaterCreatureList;
        throw new ArgumentException("Invalid creature kind: " + kind);
    }

    public bool GetEnableSnow() => HasSnow;

    public bool CanSpawnLightningBolt() => !HasSnow && HasRain;
}
