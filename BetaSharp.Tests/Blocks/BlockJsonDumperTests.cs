using System.Reflection;
using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;
using BetaSharp.Items;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;
using ItemLookup = BetaSharp.ItemLookup;

namespace BetaSharp.Tests.Blocks;

/// <summary>
///     Dumps the live, hardcoded <see cref="Block.Blocks" /> state to JSON. Env-var gated
///     (<c>DUMP_BLOCK_JSON=1</c>), normally a no-op — mirrors
///     <c>BetaSharp.Tests/Items/ItemJsonDumperTests.cs</c>.
/// </summary>
public sealed class BlockJsonDumperTests
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    private static readonly HashSet<string> s_alwaysKeepFields = ["ProtocolId", "Name"];

    private static readonly FieldInfo s_isOpaqueField = GetField(typeof(Block), "_isOpaque");
    private static readonly FieldInfo s_hasCollisionField = GetField(typeof(Block), "_hasCollision");
    private static readonly FieldInfo s_faceTextureIdsField = GetField(typeof(Block), "_faceTextureIds");
    private static readonly FieldInfo s_lootTableField = GetField(typeof(Block), "_lootTable");
    private static readonly FieldInfo s_minDroppedCountField = GetField(typeof(Block), "_minDroppedCount");
    private static readonly FieldInfo s_maxDroppedCountField = GetField(typeof(Block), "_maxDroppedCount");
    private static readonly FieldInfo s_droppedItemMetaValueField = GetField(typeof(Block), "_droppedItemMetaValue");
    private static readonly FieldInfo s_dropsWithBlockMetaField = GetField(typeof(Block), "_dropsWithBlockMeta");
    private static readonly FieldInfo s_pistonBehaviorOverrideField = GetField(typeof(Block), "_pistonBehaviorOverride");
    private static readonly FieldInfo s_blockEntityFactoryField = GetField(typeof(Block), "_blockEntityFactory");
    private static readonly FieldInfo s_lootEntriesField = GetField(typeof(LootTable), "_entries");

    private static readonly Dictionary<Type, string> s_tileEntityKeys = new()
    {
        [typeof(BlockEntityFurnace)] = "furnace",
        [typeof(BlockEntityChest)] = "chest",
        [typeof(BlockEntityRecordPlayer)] = "record_player",
        [typeof(BlockEntityDispenser)] = "dispenser",
        [typeof(BlockEntitySign)] = "sign",
        [typeof(BlockEntityMobSpawner)] = "mob_spawner",
        [typeof(BlockEntityNote)] = "note",
        [typeof(BlockEntityPiston)] = "piston",
    };

    private static FieldInfo GetField(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{type.Name}.{name} not found — did it get renamed?");

    [Fact]
    public void DumpAllBlocks()
    {
        if (Environment.GetEnvironmentVariable("DUMP_BLOCK_JSON") != "1")
        {
            return;
        }

        // Already initialized once for the whole test assembly by TestAssemblyInitializer's
        // [ModuleInitializer] — DefaultRegistries.Initialize() is not safe to call twice
        // (registries freeze on first call, a second Register() throws).
        _ = Block.Stone.Id;

        Dictionary<Block, string> fieldNames = BuildFieldNameLookup();

        string outDir = Path.Combine(FindRepoRoot(), "BetaSharp", "assets", "block");
        Directory.CreateDirectory(outDir);

        JsonElement fullDefaults = JsonSerializer.SerializeToElement(
            new BlockDefinition { ProtocolId = 0, Name = "_" }, s_options);
        JsonElement defaults = StripAlwaysKeepFields(fullDefaults);
        File.WriteAllText(Path.Combine(outDir, "_defaults.json"), JsonSerializer.Serialize(defaults, s_options));

        int count = 0;
        for (int id = 0; id < Block.Blocks.Length; id++)
        {
            Block? block = Block.Blocks[id];
            if (block is null) continue;

            if (!fieldNames.TryGetValue(block, out string? fieldName))
            {
                throw new InvalidOperationException(
                    $"Block id {id} ({block.GetBlockName()}) is not reachable from any public static Block field on Block.cs — cannot derive a unique registry name for it.");
            }

            BlockDefinition definition = Dump(block, id, ToSnakeCase(fieldName));

            JsonElement full = JsonSerializer.SerializeToElement(definition, s_options);
            JsonElement minimal = JsonMerge.StripDefaults(full, defaults, s_options, s_alwaysKeepFields);

            string path = Path.Combine(outDir, $"{definition.Name}.json");
            minimal = PreserveExistingBehaviors(path, minimal);
            File.WriteAllText(path, JsonSerializer.Serialize(minimal, s_options));
            count++;
        }

        Assert.True(count > 0, "Expected at least one Block to dump.");
    }

    private static Dictionary<Block, string> BuildFieldNameLookup()
    {
        Dictionary<Block, string> map = new(ReferenceEqualityComparer.Instance);
        foreach (FieldInfo field in typeof(Block).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(Block)) continue;
            if (field.GetValue(null) is not Block block) continue;

            // Some blocks legitimately share more than one static-field alias is not expected;
            // first writer wins if it ever happens, surfacing as a dumped file mismatch rather
            // than a crash.
            map.TryAdd(block, field.Name);
        }

        return map;
    }

    private static BlockDefinition Dump(Block block, int id, string name)
    {
        bool isOpaque = (bool)s_isOpaqueField.GetValue(block)!;
        bool hasCollision = (bool)s_hasCollisionField.GetValue(block)!;
        int?[]? faceTextureIds = (int?[]?)s_faceTextureIdsField.GetValue(block);
        LootTable? lootTable = (LootTable?)s_lootTableField.GetValue(block);
        int minDroppedCount = (int)s_minDroppedCountField.GetValue(block)!;
        int maxDroppedCount = (int)s_maxDroppedCountField.GetValue(block)!;
        int droppedItemMetaValue = (int)s_droppedItemMetaValueField.GetValue(block)!;
        bool dropsWithBlockMeta = (bool)s_dropsWithBlockMetaField.GetValue(block)!;
        PistonBehavior? pistonBehaviorOverride = (PistonBehavior?)s_pistonBehaviorOverrideField.GetValue(block);
        Func<BlockEntity>? blockEntityFactory = (Func<BlockEntity>?)s_blockEntityFactoryField.GetValue(block);

        string materialName = MaterialRegistry.TryGetName(block.Material)
            ?? throw new InvalidOperationException($"Block '{name}' has a Material not registered in MaterialRegistry.");
        string? soundGroupName = SoundGroupRegistry.TryGetName(block.SoundGroup);

        Dictionary<string, int>? faceTextures = null;
        if (faceTextureIds is not null)
        {
            faceTextures = [];
            for (int side = 0; side < faceTextureIds.Length; side++)
            {
                if (faceTextureIds[side] is { } textureId)
                {
                    faceTextures[((Side)side).ToString().ToLowerInvariant()] = textureId;
                }
            }
        }

        LootTableDefinition? lootTableDefinition = null;
        int? dropCount = null;
        if (lootTable is not null)
        {
            var entries = (LootEntry[])s_lootEntriesField.GetValue(lootTable)!;
            LootEntryDefinition[] entryDefinitions = entries
                .Select(e => new LootEntryDefinition(ReverseItemName(e.ItemId), e.Weight))
                .ToArray();
            lootTableDefinition = new LootTableDefinition(entryDefinitions, minDroppedCount, maxDroppedCount, droppedItemMetaValue);
        }
        else if (minDroppedCount != 1 || maxDroppedCount != 1)
        {
            dropCount = minDroppedCount;
        }

        string? tileEntity = null;
        if (blockEntityFactory is not null)
        {
            BlockEntity instance = blockEntityFactory();
            tileEntity = s_tileEntityKeys.TryGetValue(instance.GetType(), out string? key)
                ? key
                : throw new InvalidOperationException($"Block '{name}' has a tile entity of unmapped type {instance.GetType().Name}.");
        }

        return new BlockDefinition
        {
            ProtocolId = id,
            Name = name,
            TranslationKey = block.RegistryName == name ? null : block.RegistryName,

            Material = materialName,
            SoundGroup = soundGroupName,

            Hardness = block.Hardness,
            // SetResistance stores resistance * 3.0F internally — divide back out so the
            // round-trip through BlockFactory.Create's SetResistance call reproduces the exact
            // same stored value, not a triple-scaled one.
            Resistance = block.Resistance / 3.0F,
            Luminance = Block.BlocksLightLuminance[id] / 15.0F,
            Opacity = ReverseOpacity(id, isOpaque),
            NonOpaque = !isOpaque,
            TickRandomly = Block.BlocksRandomTick[id],
            IgnoreMetaUpdates = Block.BlocksIgnoreMetaUpdate[id],
            TrackStatistics = block.GetEnableStats(),

            TextureId = block.TextureId,
            FaceTextures = faceTextures,
            TopVariance = block.TopVariance,
            BottomVariance = block.BottomVariance,
            SideVariance = block.SideVariance,

            RenderType = block.GetRenderType().ToString(),
            RenderLayer = block.GetRenderLayer(),
            TickRate = block.GetTickRate(),
            Slipperiness = block.Slipperiness,
            NotFullCube = !block.IsFullCube(),
            NoCollision = !hasCollision,
            BoundingBox = ToBoundingBoxDefinition(block.BoundingBox),
            PistonBehavior = pistonBehaviorOverride?.ToString(),

            DropCount = dropCount,
            PreservesMetaOnDrop = dropsWithBlockMeta,
            BlockAlias = block.GetBlockAlias.Count > 0 ? [.. block.GetBlockAlias] : null,

            LootTable = lootTableDefinition,
            TileEntity = tileEntity,
        };
    }

    /// <summary>
    ///     BlockLightOpacity[id] always holds a concrete value — either the isOpaque-derived
    ///     default (255/0, applied at construction) or an explicit setOpacity(x) override applied
    ///     after. Returns -1 (meaning "no explicit override, just derive from NonOpaque") when the
    ///     stored value matches what the derived default would already produce.
    /// </summary>
    private static int ReverseOpacity(int id, bool isOpaque)
    {
        int expectedDefault = isOpaque ? 255 : 0;
        int actual = Block.BlockLightOpacity[id];
        return actual == expectedDefault ? -1 : actual;
    }

    private static BoundingBoxDefinition? ToBoundingBoxDefinition(Util.Maths.Box box)
    {
        const double e = 0.0001;
        bool isDefaultCube = Math.Abs(box.MinX) < e && Math.Abs(box.MinY) < e && Math.Abs(box.MinZ) < e
            && Math.Abs(box.MaxX - 1) < e && Math.Abs(box.MaxY - 1) < e && Math.Abs(box.MaxZ - 1) < e;
        if (isDefaultCube) return null;

        return new BoundingBoxDefinition(
            (float)box.MinX, (float)box.MinY, (float)box.MinZ,
            (float)box.MaxX, (float)box.MaxY, (float)box.MaxZ);
    }

    /// <summary>
    ///     Loot entries commonly name another BLOCK (Stone drops "cobblestone") as well as
    ///     standalone items — DefaultRegistries.Items only covers the JSON item registry
    ///     (protocol ids 256+), not block-derived item ids (0-255). ItemLookup already resolves
    ///     both (it backs /give and recipes), so the dumper reverse-lookup and BlockFactory's
    ///     forward lookup both go through it instead of a second, block-unaware mechanism.
    /// </summary>
    private static string ReverseItemName(int itemId)
    {
        ItemLookup.Initialize();
        return ItemLookup.ResolveItemName(new ItemStack(itemId, 1, 0));
    }

    private static string ToSnakeCase(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)(?<![A-Z])(?=[A-Z])", "_").ToLowerInvariant();

    /// <summary>
    ///     The dumper can never reconstruct "Behaviors" (see the class doc) — it always computes an
    ///     empty one. If <paramref name="path" /> already has a non-empty hand-authored "Behaviors"
    ///     block, carry it forward so re-running the dumper (e.g. after a flat-field change) doesn't
    ///     wipe out that manual work.
    /// </summary>
    private static JsonElement PreserveExistingBehaviors(string path, JsonElement minimal)
    {
        if (!File.Exists(path)) return minimal;

        JsonElement existing = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path), s_options);
        if (!existing.TryGetProperty("Behaviors", out JsonElement existingBehaviors) ||
            !existingBehaviors.EnumerateObject().Any())
        {
            return minimal;
        }

        var merged = new Dictionary<string, JsonElement>();
        foreach (JsonProperty property in minimal.EnumerateObject())
        {
            merged[property.Name] = property.Value;
        }

        merged["Behaviors"] = existingBehaviors;
        return JsonSerializer.SerializeToElement(merged, s_options);
    }

    private static JsonElement StripAlwaysKeepFields(JsonElement full)
    {
        var kept = new Dictionary<string, JsonElement>();
        foreach (JsonProperty property in full.EnumerateObject())
        {
            if (!s_alwaysKeepFields.Contains(property.Name))
            {
                kept[property.Name] = property.Value;
            }
        }

        return JsonSerializer.SerializeToElement(kept, s_options);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root (no .git ancestor found).");
    }
}
