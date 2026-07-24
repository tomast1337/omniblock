using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Registries.Data;

namespace BetaSharp.Blocks;

/// <summary>
///     A record (not a plain class) so tests can clone one onto a scratch ProtocolId via
///     <c>with</c>. Implements <see cref="IDataAsset" /> directly rather than extending
///     <see cref="DataAsset" /> — records can only inherit from another record, not a plain
///     class (CS8864). <c>Name</c>/<c>Namespace</c> are <c>[JsonIgnore]</c>d and set by
///     <see cref="BlockDefinitionJsonLoader" /> from the JSON filename, never read from the file
///     itself.
/// </summary>
public sealed record BlockDefinition : IDataAsset
{
    [JsonIgnore]
    public string Name { get; set; } = "";

    [JsonIgnore]
    public Namespace Namespace { get; set; } = Namespace.BetaSharp;

    public required int ProtocolId { get; init; }
    public string? TranslationKey { get; init; }

    public string Material { get; init; } = "stone";
    public string? SoundGroup { get; init; }

    // Hardness == -1 is unbreakable (SetUnbreakable() is just sugar for SetHardness(-1)) —
    // no separate flag needed.
    public float Hardness { get; init; }
    public float Resistance { get; init; }
    public float Luminance { get; init; }
    public int Opacity { get; init; } = -1;
    public bool NonOpaque { get; init; }
    public bool TickRandomly { get; init; }
    public bool IgnoreMetaUpdates { get; init; }
    public bool TrackStatistics { get; init; } = true;

    public int TextureId { get; init; }
    public Dictionary<string, int>? FaceTextures { get; init; }
    public TextureVariance TopVariance { get; init; }
    public TextureVariance BottomVariance { get; init; }
    public TextureVariance SideVariance { get; init; }

    public string RenderType { get; init; } = "Standard";
    public int RenderLayer { get; init; }
    public int TickRate { get; init; } = 10;
    public float Slipperiness { get; init; } = 0.6F;
    public bool NotFullCube { get; init; }
    public bool NoCollision { get; init; }
    public BoundingBoxDefinition? BoundingBox { get; init; }
    public string? PistonBehavior { get; init; }

    public int? DropCount { get; init; }
    public bool PreservesMetaOnDrop { get; init; }
    public string[]? BlockAlias { get; init; }

    public LootTableDefinition? LootTable { get; init; }
    public string? TileEntity { get; init; }

    // 0 = not flammable. Never above 100 (see FireBehavior), so byte is sufficient.
    public byte BurnChance { get; init; }
    public byte SpreadChance { get; init; }

    /// <summary>
    ///     One entry per behavior INSTANCE, not per slot — a class can implement several
    ///     capability interfaces at once (e.g. <c>PlantSurvivalBehavior</c> implements both
    ///     Ticker and Physics), and a block wanting that one instance wired into several of its
    ///     capability slots ("Ticker", "Physics", "Lifecycle", "Visuals", "Interactable",
    ///     "Redstone") lists them all in that entry's "Slots" array rather than repeating the
    ///     params once per slot. Each entry's JSON carries "Slots" and "Type" (the
    ///     <c>BehaviorRegistry</c> key) alongside its own params, e.g.
    ///     <c>[{"Slots": ["Ticker"], "Type": "sapling"}, {"Slots": ["Physics"], "Type": "plant_survival"}]</c>.
    /// </summary>
    public List<JsonElement> Behaviors { get; init; } = [];
}

public sealed record BoundingBoxDefinition(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);
