using System.Text.Json.Serialization;
using OmniBlock.Registries.Data;

namespace OmniBlock.Blocks.Materials;

/// <summary>
///     JSON shape of <c>assets/material/*.json</c>. Converted once by
///     <see cref="MaterialRegistry" /> into the canonical <see cref="Material" /> instance.
/// </summary>
public sealed class MaterialDefinition : DataAsset
{
    /// <summary>Name of a <see cref="Worlds.Maps.MapColor" /> static property, e.g. <c>"grass"</c>.</summary>
    public string MapColor { get; set; } = "";

    public bool IsFluid { get; set; }
    public bool IsSolid { get; set; } = true;
    public bool BlocksVision { get; set; } = true;
    public bool BlocksMovement { get; set; } = true;
    public bool IsBurnable { get; set; }
    public bool IsReplaceable { get; set; }
    public bool IsHandHarvestable { get; set; } = true;
    public bool IsTransparent { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<PistonBehavior>))]
    public PistonBehavior PistonBehavior { get; set; }
}
