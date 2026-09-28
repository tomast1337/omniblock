using OmniBlock.Blocks;
using OmniBlock.Worlds.Lod;
using Face = OmniBlock.Client.Rendering.Chunks.Lod.TerrainLodShapeGeometry.Face;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal static class TerrainLodFenceGeometry
{
    private static readonly Face[][] Bodies = Enumerable.Range(0, 16)
        .Select(mask => TerrainLodShapeGeometry.Boxes(FenceShape.GetBounds(mask))).ToArray();
    private static readonly Face[][] Rails = new[] { Side.West, Side.East, Side.North, Side.South }
        .Select(side => TerrainLodShapeGeometry.Boxes(FenceShape.GetRails(side))).ToArray();

    internal static bool Connects(TerrainLodMaterial first, TerrainLodMaterial second) =>
        first.Geometry == TerrainLodGeometryClass.Fence && second.Geometry == TerrainLodGeometryClass.Fence &&
        first.BlockId == second.BlockId;
    internal static ReadOnlySpan<Face> Body(int mask) => Bodies[mask & 15];
    internal static ReadOnlySpan<Face> Rail(Side side) => Rails[side switch
    {
        Side.West => 0, Side.East => 1, Side.North => 2, Side.South => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    }];
}
