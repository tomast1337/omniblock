using BetaSharp.Registries.Data;

namespace BetaSharp;

public sealed class ArmorMaterialDefinition : DataAsset
{
    public int ArmorLevel { get; init; }

    /// <summary>
    ///     Stem of the armor overlay textures, read by the renderer as <c>/armor/{prefix}_1.png</c>
    ///     and <c>_2.png</c>. A name rather than an index so a material defined outside this
    ///     assembly can ship its own textures.
    /// </summary>
    public string TexturePrefix { get; init; } = "";
}
