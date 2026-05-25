using System.Text.Json.Serialization;

namespace BetaSharp.Client.Rendering.Entities.Bbmodel;

public sealed class BbmodelDocument
{
    public BbmodelMeta Meta { get; set; } = null!;
    public string? Name { get; set; }
    public BbmodelResolution? Resolution { get; set; }
    public List<BbmodelElement> Elements { get; set; } = [];
    public List<BbmodelGroup> Groups { get; set; } = [];
    public List<BbmodelOutlinerEntry> Outliner { get; set; } = [];
}

public sealed class BbmodelOutlinerEntry
{
    public string Uuid { get; set; } = "";
    public List<string> Children { get; set; } = [];
}

public sealed class BbmodelMeta
{
    [JsonPropertyName("format_version")]
    public string FormatVersion { get; set; } = "";

    [JsonPropertyName("model_format")]
    public string ModelFormat { get; set; } = "";

    [JsonPropertyName("box_uv")]
    public bool BoxUv { get; set; }
}

public sealed class BbmodelResolution
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class BbmodelElement
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Uuid { get; set; } = "";
    public float[] From { get; set; } = [];
    public float[] To { get; set; } = [];
    public float[] Origin { get; set; } = [];
    [JsonPropertyName("uv_offset")]
    public int[] UvOffset { get; set; } = [];
    public float Inflate { get; set; }
    [JsonPropertyName("mirror_uv")]
    public bool MirrorUv { get; set; }
    public bool Export { get; set; } = true;
}

public sealed class BbmodelGroup
{
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public float[] Origin { get; set; } = [];
    public bool Export { get; set; } = true;
}

/// <summary>Java-space geometry for a single bone after BB conversion.</summary>
public readonly record struct BbmodelPartGeometry(
    string Name,
    float PivotX,
    float PivotY,
    float PivotZ,
    float BoxX,
    float BoxY,
    float BoxZ,
    int SizeX,
    int SizeY,
    int SizeZ,
    int UvU,
    int UvV,
    float Inflate,
    bool Mirror);

public sealed class BbmodelBuiltModel
{
    public required IReadOnlyDictionary<string, Models.ModelPart> Parts { get; init; }
    public required IReadOnlyList<string> RenderOrder { get; init; }
    public required IReadOnlyDictionary<string, BbmodelPartGeometry> Geometry { get; init; }
}
