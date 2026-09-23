using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Registries.Data;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Blocks;

public sealed record BlockDefinition : IDataAsset
{
    /// <summary>Explicit protocol ID, or -1 to allocate one deterministically.</summary>
    public int ProtocolId { get; init; } = -1;

    public string? TranslationKey { get; init; }

    public string Material { get; init; } = "stone";
    public string? SoundGroup { get; init; }

    public float Hardness { get; init; }
    public float Resistance { get; init; }
    public float Luminance { get; init; }
    public int Opacity { get; init; } = -1;
    public bool NonOpaque { get; init; }
    public bool TickRandomly { get; init; }
    public bool IgnoreMetaUpdates { get; init; }
    public bool TrackStatistics { get; init; } = true;

    public string TextureId { get; init; } = "";
    public Dictionary<string, string>? FaceTextures { get; init; }
    public TextureVariance TopVariance { get; init; }
    public TextureVariance BottomVariance { get; init; }
    public TextureVariance SideVariance { get; init; }

    public string RenderType { get; init; } = "Standard";
    public int RenderLayer { get; init; }
    public BlockTerrainLodDefinition? TerrainLod { get; init; }
    public int TickRate { get; init; } = 10;
    public float Slipperiness { get; init; } = 0.6F;
    public bool NotFullCube { get; init; }
    public bool NoCollision { get; init; }
    public BoundingBoxDefinition? BoundingBox { get; init; }
    public string? PistonBehavior { get; init; }

    public int? DropCount { get; init; }
    public bool PreservesMetaOnDrop { get; init; }
    public string[]? BlockAlias { get; init; }
    public BlockItemDefinition BlockItem { get; init; } = new();

    public LootTableDefinition? LootTable { get; init; }
    public string? TileEntity { get; init; }

    public byte BurnChance { get; init; }
    public byte SpreadChance { get; init; }

    public List<JsonElement> Behaviors { get; init; } = [];

    [JsonIgnore] public string Name { get; set; } = "";

    [JsonIgnore] public Namespace Namespace { get; set; } = Namespace.OmniBlock;
}

public sealed record BoundingBoxDefinition(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);

/// <summary>
///     Resource-pack-independent distant-terrain representation for blocks whose rendering cannot
///     be inferred safely. Omitting the descriptor retains the built-in classifier; declaring an
///     empty descriptor deliberately selects the conservative-cube fallback.
/// </summary>
public sealed record BlockTerrainLodDefinition
{
    public string Geometry { get; init; } = nameof(TerrainLodGeometryClass.ConservativeCube);
    public bool? OccludesFaces { get; init; }
    public int? MaxSampleSize { get; init; }
}

/// <summary>Validated immutable form stored on a finalized runtime block.</summary>
public readonly record struct BlockTerrainLodDescriptor(
    TerrainLodGeometryClass Geometry,
    bool OccludesFaces,
    int MaxSampleSize = int.MaxValue);

public sealed record BlockItemDefinition
{
    public string Type { get; init; } = "block";
    public string? TranslationKey { get; init; }
    public string[] Aliases { get; init; } = [];
}
