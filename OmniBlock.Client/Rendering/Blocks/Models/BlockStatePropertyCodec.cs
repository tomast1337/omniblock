namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Build-time view of named block-state properties over legacy metadata. These properties do not
/// change the wire or save representation. A value is absent for metadata that never represented
/// that material, so an authoring selector cannot accidentally reinterpret old invalid states.
/// </summary>
internal static class BlockStatePropertyCodec
{
    internal static bool Supports(ResourceLocation block, string property) =>
        block.Namespace == "omniblock" && block.Path switch
        {
            "slab" => property is "slab.material" or "slab.half",
            "double_slab" => property == "slab.material",
            _ => false
        };

    internal static bool Accepts(string property, string value) => property switch
    {
        "slab.material" => value is "stone" or "sandstone" or "wood" or "cobblestone",
        "slab.half" => value is "lower" or "upper",
        _ => false
    };

    internal static string? Get(ResourceLocation block, int metadata, string property)
    {
        if (!Supports(block, property) || metadata is < 0 or > 15) return null;
        if (property == "slab.half") return (metadata & 8) == 0 ? "lower" : "upper";
        // The shipped double-slab renderer recognizes materials only in metadata 0..3.
        // Keeping 4..15 unclaimed preserves the legacy fallback model for old saves.
        if (block.Path == "double_slab" && metadata > 3) return null;
        return (metadata & 7) switch
        {
            0 => "stone",
            1 => "sandstone",
            2 => "wood",
            3 => "cobblestone",
            _ => null
        };
    }
}
