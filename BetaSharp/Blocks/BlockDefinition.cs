using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Registries.Data;

namespace BetaSharp.Blocks;

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

    public byte BurnChance { get; init; }
    public byte SpreadChance { get; init; }

    public List<JsonElement> Behaviors { get; init; } = [];
}

public sealed record BoundingBoxDefinition(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);
