using BetaSharp.Registries.Data;

namespace BetaSharp;

/// <summary>
/// JSON shape of <c>assets/sound_group/*.json</c>. Converted once by
/// <see cref="SoundGroupRegistry"/> into the canonical <see cref="BlockSoundGroup"/> instance.
/// </summary>
public sealed class SoundGroupDefinition : DataAsset
{
    public string GroupName { get; set; } = "";
    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public string? CustomBreakSound { get; set; }
}
