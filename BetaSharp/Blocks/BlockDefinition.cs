using System.Text.Json;

namespace BetaSharp.Blocks;

public sealed class BlockDefinition
{
    public required int ProtocolId { get; init; }
    public required string Name { get; init; }
    public string? TranslationKey { get; init; }

    public string Material { get; init; } = "stone";
    public string? SoundGroup { get; init; }

    public float Hardness { get; init; }
    public float Resistance { get; init; }
    public float Luminance { get; init; }
    public int Opacity { get; init; } = -1;
    public bool TickRandomly { get; init; }
    public bool Unbreakable { get; init; }
    public bool IgnoreMetaUpdates { get; init; }
    public bool TrackStatistics { get; init; } = true;

    public int TextureId { get; init; }
    public int? TopTextureId { get; init; }
    public int? BottomTextureId { get; init; }
    public TextureVariance TopVariance { get; init; }
    public TextureVariance BottomVariance { get; init; }
    public TextureVariance SideVariance { get; init; }

    public LootTableDefinition? LootTable { get; init; }
    public string? TileEntity { get; init; }

    public Dictionary<string, JsonElement> Behaviors { get; init; } = [];
}
