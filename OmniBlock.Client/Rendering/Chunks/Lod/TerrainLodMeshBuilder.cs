using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Registries;
using OmniBlock.Textures;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>CPU geometry for one detail level of one distant chunk column.</summary>
internal sealed record TerrainLodMeshData(
    int Level,
    ChunkVertex[] Vertices,
    ChunkLightVertex[] Lights)
{
    public long EstimatedBytes =>
        (long)Vertices.Length * WgpuMesh.ChunkVertexStride +
        (long)Lights.Length * WgpuMesh.ChunkLightVertexStride;
}

/// <summary>
///     Compiles the resource-pack-independent hierarchy into ordinary terrain-array quads. Phase 5
///     starts with opaque materials; cutout foliage and liquids remain separate later passes.
/// </summary>
internal static class TerrainLodMeshBuilder
{
    private static readonly float VerticalOrigin = ChuckFormat.WorldHeight / 2.0f;

    public static TerrainLodMeshData Build(
        TerrainLodHierarchy hierarchy,
        int levelIndex,
        IBlockRuntimeView blocks,
        bool hasSkyLight)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(blocks);
        if ((uint)levelIndex >= (uint)hierarchy.Levels.Count)
            throw new ArgumentOutOfRangeException(nameof(levelIndex));

        var level = hierarchy.Levels[levelIndex];
        List<ChunkVertex> vertices = [];
        List<ChunkLightVertex> lights = [];
        var light = hasSkyLight
            ? new ChunkLightVertex(ChunkVertexHelper.ToQuarterLevels(15), 0)
            : new ChunkLightVertex(0, 0);

        for (var x = 0; x < level.Width; x++)
        for (var z = 0; z < level.Depth; z++)
        for (var y = 0; y < level.Height; y++)
        {
            var cell = level[x, y, z];
            if (cell.IsEmpty || !IsOpaque(cell.Primary)) continue;
            if (!blocks.TryGet(cell.Primary.BlockId, out var block) || block is null) continue;

            var minX = x * level.Scale;
            var minY = y * level.Scale - VerticalOrigin;
            var minZ = z * level.Scale;
            var maxX = Math.Min((x + 1) * level.Scale, 16);
            var maxY = Math.Min((y + 1) * level.Scale, ChuckFormat.WorldHeight) - VerticalOrigin;
            var maxZ = Math.Min((z + 1) * level.Scale, 16);
            var tileX = maxX - minX;
            var tileY = maxY - minY;
            var tileZ = maxZ - minZ;

            if ((cell.ExposedFaces & TerrainLodFaceMask.Down) != 0)
                AddFace(Side.Down, 0.5f, tileX, tileZ,
                    (minX, minY, maxZ), (minX, minY, minZ),
                    (maxX, minY, minZ), (maxX, minY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.Up) != 0)
                AddFace(Side.Up, 1.0f, tileX, tileZ,
                    (maxX, maxY, maxZ), (maxX, maxY, minZ),
                    (minX, maxY, minZ), (minX, maxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.West) != 0)
                AddFace(Side.West, 0.6f, tileZ, tileY,
                    (minX, maxY, minZ), (minX, minY, minZ),
                    (minX, minY, maxZ), (minX, maxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.East) != 0)
                AddFace(Side.East, 0.6f, tileZ, tileY,
                    (maxX, maxY, maxZ), (maxX, minY, maxZ),
                    (maxX, minY, minZ), (maxX, maxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.North) != 0)
                AddFace(Side.North, 0.8f, tileX, tileY,
                    (maxX, maxY, minZ), (maxX, minY, minZ),
                    (minX, minY, minZ), (minX, maxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.South) != 0)
                AddFace(Side.South, 0.8f, tileX, tileY,
                    (minX, maxY, maxZ), (minX, minY, maxZ),
                    (maxX, minY, maxZ), (maxX, maxY, maxZ));

            void AddFace(
                Side side,
                float shade,
                float tileU,
                float tileV,
                (float X, float Y, float Z) a,
                (float X, float Y, float Z) b,
                (float X, float Y, float Z) c,
                (float X, float Y, float Z) d)
            {
                var texture = block.GetTexture(side, cell.Primary.Metadata);
                var layer = Atlases.Terrain.LayerOfGridIndex(texture);
                var channel = (byte)Math.Clamp((int)MathF.Round(shade * 255.0f), 0, 255);
                var color = PackColor(channel, channel, channel, byte.MaxValue);
                vertices.Add(ChunkVertexHelper.Create(color, a.X, a.Y, a.Z, tileU, 0, layer));
                vertices.Add(ChunkVertexHelper.Create(color, b.X, b.Y, b.Z, tileU, tileV, layer));
                vertices.Add(ChunkVertexHelper.Create(color, c.X, c.Y, c.Z, 0, tileV, layer));
                vertices.Add(ChunkVertexHelper.Create(color, d.X, d.Y, d.Z, 0, 0, layer));
                lights.Add(light);
                lights.Add(light);
                lights.Add(light);
                lights.Add(light);
            }
        }

        return new TerrainLodMeshData(levelIndex, [.. vertices], [.. lights]);
    }

    internal static bool IsOpaque(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Opaque or
            TerrainLodGeometryClass.ConservativeCube;

    private static int PackColor(byte red, byte green, byte blue, byte alpha) =>
        BitConverter.IsLittleEndian
            ? alpha << 24 | blue << 16 | green << 8 | red
            : red << 24 | green << 16 | blue << 8 | alpha;
}
