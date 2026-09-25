using System.Numerics;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
///     Geometry detail and texture detail are separate: exact and 1x1 terrain keep crisp level
///     zero, while opaque aggregate cells sample a prefiltered tile. Thin, cutout and translucent
///     materials stay at level zero until they have material-specific distant representations.
/// </summary>
internal static class TerrainLodTextureDetail
{
    internal const int MaximumFilteredLevel = 4; // the shipped terrain tile is at least 16x16

    public static byte MipLevel(TerrainLodMaterial material, int sampleSize)
    {
        if (material.Geometry != TerrainLodGeometryClass.Opaque || sampleSize <= 1)
            return 0;
        if (!BitOperations.IsPow2((uint)sampleSize))
            throw new ArgumentOutOfRangeException(nameof(sampleSize));
        return (byte)Math.Min(MaximumFilteredLevel,
            BitOperations.Log2((uint)sampleSize));
    }
}
