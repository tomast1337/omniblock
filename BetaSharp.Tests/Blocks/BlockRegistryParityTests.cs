using System.Reflection;
using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Materials;
using BetaSharp.Registries.Data;

namespace BetaSharp.Tests.Blocks;

/// <summary>
///     Verifies the dumped/hand-annotated JSON in <c>assets/block/</c> reproduces the
///     live, hardcoded <see cref="Block" /> state field-for-field.
///     <para>
///         <see cref="Block.Blocks" /> is a single global array — the real <see cref="Block" />
///         constructor throws if a slot is already occupied
///         (<c>"Slot {id} is already occupied..."</c>), so a JSON-driven block can never be built
///         at the SAME id as the hardcoded one it's being compared against in the same process.
///         Instead, each definition is cloned onto a scratch id (a slot no hardcoded block
///         occupies) via <c>with</c>, built there, compared against a snapshot taken from the real
///         id beforehand, and the scratch slot is reset to null afterward.
///     </para>
/// </summary>
public sealed class BlockRegistryParityTests
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    private static readonly FieldInfo s_hasCollisionField = typeof(Block).GetField("_hasCollision", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void VerifyJsonMatchesHardcodedStatics()
    {
        _ = Block.Stone.Id;

        string genDir = Path.Combine(FindRepoRoot(), "BetaSharp", "assets", "block");
        Assert.True(Directory.Exists(genDir), $"Expected {genDir} to exist — run the dumper first (DUMP_BLOCK_JSON=1).");

        JsonElement defaults = JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(Path.Combine(genDir, "_defaults.json")), s_options);

        var failures = new List<string>();
        int verified = 0;

        foreach (string path in Directory.GetFiles(genDir, "*.json"))
        {
            if (Path.GetFileNameWithoutExtension(path) == "_defaults") continue;

            BlockDefinition definition = LoadDefinition(path, defaults);
            Block? live = Block.Blocks[definition.ProtocolId];
            Assert.True(live is not null, $"{definition.Name}: ProtocolId {definition.ProtocolId} has no hardcoded block to compare against.");

            Snapshot expected = Snapshot.Capture(live!, definition.ProtocolId);

            int scratchId = NextFreeId();
            try
            {
                BlockDefinition scratchDefinition = definition with { ProtocolId = scratchId };
                Block scratchBlock = BlockFactory.Create(scratchDefinition);
                BlockFactory.AttachBehaviors(scratchBlock, scratchDefinition);

                Snapshot actual = Snapshot.Capture(scratchBlock, scratchId);
                string? mismatch = expected.DiffAgainst(actual);
                if (mismatch is not null)
                {
                    failures.Add($"{definition.Name}: {mismatch}");
                }
                else
                {
                    verified++;
                }
            }
            finally
            {
                ClearScratchSlot(scratchId);
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} block(s) failed parity:\n" + string.Join("\n", failures));
        Assert.True(verified > 0, "Expected at least one block to be verified.");
    }

    private static BlockDefinition LoadDefinition(string path, JsonElement defaults)
    {
        JsonElement raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path), s_options);
        JsonElement merged = JsonMerge.Merge(defaults, raw, s_options);
        return JsonSerializer.Deserialize<BlockDefinition>(merged.GetRawText(), s_options)
            ?? throw new InvalidOperationException($"Failed to deserialize {path}");
    }

    /// <summary>
    ///     Scans from the top of the id space down, since real blocks cluster at the low end.
    ///     Each call's slot is cleared by <see cref="ClearScratchSlot" /> before the next call, so
    ///     the same handful of ids get reused across all 96 definitions rather than needing 96
    ///     simultaneously-free slots.
    /// </summary>
    private static int NextFreeId()
    {
        for (int id = Block.Blocks.Length - 1; id >= 0; id--)
        {
            if (Block.Blocks[id] is null) return id;
        }

        throw new InvalidOperationException("No free scratch block id available.");
    }

    private static void ClearScratchSlot(int id)
    {
        Block.Blocks[id] = null!;
        Block.BlocksOpaque[id] = false;
        Block.BlockLightOpacity[id] = 0;
        Block.BlocksAllowVision[id] = false;
        Block.BlocksWithEntity[id] = false;
        Block.BlocksLightLuminance[id] = 0;
        Block.BlocksRandomTick[id] = false;
        Block.BlocksIgnoreMetaUpdate[id] = false;
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

    /// <summary>
    ///     Everything compared between the hardcoded block and its JSON-rebuilt counterpart.
    ///     Deliberately excludes <c>Block.BoundingBox</c> — for blocks with a dynamic
    ///     <see cref="IBlockPhysics.UpdateBoundingBox" /> override (torches, ladders, buttons, ...
    ///     anything wall-mounted), that field is query-time scratch state on the shared singleton,
    ///     last-write-wins across whatever earlier test in the process happened to query a
    ///     position for it — not a stable per-block-type value safe to snapshot-compare.
    /// </summary>
    private sealed record Snapshot(
        float Hardness,
        float Resistance,
        int TextureId,
        string? MaterialName,
        string RenderType,
        int TickRate,
        int RenderLayer,
        float Slipperiness,
        bool HasCollision,
        bool FullCube,
        bool Opaque,
        int LightOpacity,
        bool RandomTick,
        bool IgnoreMetaUpdate,
        int LightLuminance,
        bool AllowVision,
        Type? TickerType,
        Type? PhysicsType,
        Type? LifecycleType,
        Type? VisualsType,
        Type? InteractableType,
        Type? RedstoneType)
    {
        public static Snapshot Capture(Block block, int id) => new(
            block.Hardness,
            block.Resistance,
            block.TextureId,
            MaterialRegistry.TryGetName(block.Material),
            block.GetRenderType().ToString(),
            block.GetTickRate(),
            block.GetRenderLayer(),
            block.Slipperiness,
            (bool)s_hasCollisionField.GetValue(block)!,
            block.IsFullCube(),
            Block.BlocksOpaque[id],
            Block.BlockLightOpacity[id],
            Block.BlocksRandomTick[id],
            Block.BlocksIgnoreMetaUpdate[id],
            Block.BlocksLightLuminance[id],
            Block.BlocksAllowVision[id],
            block.Ticker?.GetType(),
            block.Physics?.GetType(),
            block.Lifecycle?.GetType(),
            block.Visuals?.GetType(),
            block.Interactable?.GetType(),
            block.Redstone?.GetType());

        /// <summary>Returns null if identical, otherwise a human-readable description of every mismatch.</summary>
        public string? DiffAgainst(Snapshot other)
        {
            var diffs = new List<string>();
            void Check<T>(string name, T expected, T actual)
            {
                if (!Equals(expected, actual)) diffs.Add($"{name}: expected {expected}, got {actual}");
            }

            Check(nameof(Hardness), Hardness, other.Hardness);
            Check(nameof(Resistance), Resistance, other.Resistance);
            Check(nameof(TextureId), TextureId, other.TextureId);
            Check(nameof(MaterialName), MaterialName, other.MaterialName);
            Check(nameof(RenderType), RenderType, other.RenderType);
            Check(nameof(TickRate), TickRate, other.TickRate);
            Check(nameof(RenderLayer), RenderLayer, other.RenderLayer);
            Check(nameof(Slipperiness), Slipperiness, other.Slipperiness);
            Check(nameof(HasCollision), HasCollision, other.HasCollision);
            Check(nameof(FullCube), FullCube, other.FullCube);
            Check(nameof(Opaque), Opaque, other.Opaque);
            Check(nameof(LightOpacity), LightOpacity, other.LightOpacity);
            Check(nameof(RandomTick), RandomTick, other.RandomTick);
            Check(nameof(IgnoreMetaUpdate), IgnoreMetaUpdate, other.IgnoreMetaUpdate);
            Check(nameof(LightLuminance), LightLuminance, other.LightLuminance);
            Check(nameof(AllowVision), AllowVision, other.AllowVision);
            Check(nameof(TickerType), TickerType, other.TickerType);
            Check(nameof(PhysicsType), PhysicsType, other.PhysicsType);
            Check(nameof(LifecycleType), LifecycleType, other.LifecycleType);
            Check(nameof(VisualsType), VisualsType, other.VisualsType);
            Check(nameof(InteractableType), InteractableType, other.InteractableType);
            Check(nameof(RedstoneType), RedstoneType, other.RedstoneType);

            return diffs.Count == 0 ? null : string.Join("; ", diffs);
        }
    }
}
