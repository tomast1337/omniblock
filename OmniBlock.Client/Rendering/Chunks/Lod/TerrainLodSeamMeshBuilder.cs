using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Registries;
using OmniBlock.Textures;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>CPU geometry for one independently replaceable LOD column boundary.</summary>
internal sealed record TerrainLodSeamMeshData(
    ChunkVertex[] Vertices,
    ChunkLightVertex[] Lights)
{
    public long EstimatedBytes =>
        (long)Vertices.Length * WgpuMesh.ChunkVertexStride +
        (long)Lights.Length * WgpuMesh.ChunkLightVertexStride;
}

/// <summary>
///     Compiles the solid/cutout surface shared by two adjacent LOD columns. The result is owned by
///     the boundary rather than either column, so a selected-level change can replace the seam
///     without reconverting both complete column meshes.
/// </summary>
/// <remarks>
///     Boundary ownership is canonical: callers submit the west column followed by its east
///     neighbor, or the north column followed by its south neighbor. Geometry is emitted in the
///     first column's local coordinate space. Liquid height reconciliation deliberately remains a
///     separate presentation layer because two blended surfaces cannot safely share this policy.
/// </remarks>
internal static class TerrainLodSeamMeshBuilder
{
    private static readonly float VerticalOrigin = ChuckFormat.WorldHeight / 2.0f;

    public static TerrainLodSeamMeshData BuildSolid(
        TerrainLodBoundarySummary owner,
        int ownerLevel,
        TerrainLodBoundarySummary neighbor,
        int neighborLevel,
        Side ownerSide,
        IBlockRuntimeView blocks,
        bool hasSkyLight,
        ILightProvider? lighting = null,
        IBlockReader? visuals = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(neighbor);
        ArgumentNullException.ThrowIfNull(blocks);
        ValidateAdjacency(owner, neighbor, ownerSide);
        if (!owner.HasLevel(ownerLevel) || !neighbor.HasLevel(neighborLevel))
            throw new ArgumentException(
                $"Terrain LOD seam selected an unavailable level: owner {ownerLevel}, neighbor {neighborLevel}.");
        if (Math.Abs(ownerLevel - neighborLevel) > 1)
            throw new ArgumentException(
                $"Terrain LOD seam levels must be equal or adjacent, got {ownerLevel} and {neighborLevel}.");

        var neighborSide = ownerSide == Side.East ? Side.West : Side.North;
        var fineLevel = Math.Min(ownerLevel, neighborLevel);
        var fineScale = 1 << fineLevel;
        var ownerScale = 1 << ownerLevel;
        var neighborScale = 1 << neighborLevel;
        var fallbackLight = hasSkyLight
            ? new ChunkLightVertex(ChunkVertexHelper.ToQuarterLevels(15), 0)
            : new ChunkLightVertex(0, 0);
        List<ChunkVertex> vertices = [];
        List<ChunkLightVertex> lights = [];
        blocks.TryGet("omniblock:grass_block", out var grassBlock);
        var grassOverlayTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");

        for (var along = 0; along < 16; along += fineScale)
        for (var y = 0; y < ChuckFormat.WorldHeight; y += fineScale)
        {
            if (!owner.TryGet(ownerLevel, ownerSide,
                    along / ownerScale, y / ownerScale, false, out var ownerMaterial) ||
                !neighbor.TryGet(neighborLevel, neighborSide,
                    along / neighborScale, y / neighborScale, false, out var neighborMaterial))
                continue;

            var ownerVisible = TerrainLodMeshBuilder.IsDepthWriting(ownerMaterial) &&
                               (neighborMaterial.IsAir || !neighborMaterial.OccludesFaces);
            var neighborVisible = TerrainLodMeshBuilder.IsDepthWriting(neighborMaterial) &&
                                  (ownerMaterial.IsAir || !ownerMaterial.OccludesFaces);
            if (!ownerVisible && !neighborVisible) continue;

            // Two non-occluding depth materials could each request the same coplanar boundary.
            // Select the canonical owner's face so arrival order cannot create double surfaces or
            // z-fighting. An opaque material still wins over a cutout material on either side.
            var useOwner = ownerVisible && (!neighborVisible || ownerMaterial.OccludesFaces ||
                                            !neighborMaterial.OccludesFaces);
            var material = useOwner ? ownerMaterial : neighborMaterial;
            if (!blocks.TryGet(material.BlockId, out var block) || block is null) continue;

            var minAlong = (float)along;
            var maxAlong = Math.Min(along + fineScale, 16);
            var minY = y - VerticalOrigin;
            var maxY = Math.Min(y + fineScale, ChuckFormat.WorldHeight) - VerticalOrigin;
            var faceSide = useOwner ? ownerSide : neighborSide;
            var sampleX = owner.ChunkX * 16 + (ownerSide == Side.East ? 16 : (minAlong + maxAlong) * 0.5f);
            var sampleZ = owner.ChunkZ * 16 + (ownerSide == Side.South ? 16 : (minAlong + maxAlong) * 0.5f);
            var sampleY = y + fineScale * 0.5f;
            var tint = visuals is null
                ? block.GetColorForFace(material.Metadata, (int)faceSide)
                : block.GetColorMultiplier(
                    visuals, (int)MathF.Floor(sampleX), (int)MathF.Floor(sampleY),
                    (int)MathF.Floor(sampleZ), material.Metadata);
            var appearance = TerrainLodMeshBuilder.ResolveFaceAppearance(
                block, material.Metadata, faceSide, tint,
                ReferenceEquals(block, grassBlock), grassOverlayTexture);
            var light = SampleLight(faceSide, block.LightEmission, sampleX, sampleY, sampleZ);

            if (ownerSide == Side.East)
            {
                const float x = 16;
                if (useOwner)
                    AddFace(0.6f, maxAlong - minAlong, maxY - minY,
                        (x, maxY, maxAlong), (x, minY, maxAlong),
                        (x, minY, minAlong), (x, maxY, minAlong));
                else
                    AddFace(0.6f, maxAlong - minAlong, maxY - minY,
                        (x, maxY, minAlong), (x, minY, minAlong),
                        (x, minY, maxAlong), (x, maxY, maxAlong));
            }
            else
            {
                const float z = 16;
                if (useOwner)
                    AddFace(0.8f, maxAlong - minAlong, maxY - minY,
                        (minAlong, maxY, z), (minAlong, minY, z),
                        (maxAlong, minY, z), (maxAlong, maxY, z));
                else
                    AddFace(0.8f, maxAlong - minAlong, maxY - minY,
                        (maxAlong, maxY, z), (maxAlong, minY, z),
                        (minAlong, minY, z), (minAlong, maxY, z));
            }

            void AddFace(
                float shade,
                float tileU,
                float tileV,
                (float X, float Y, float Z) a,
                (float X, float Y, float Z) b,
                (float X, float Y, float Z) c,
                (float X, float Y, float Z) d)
            {
                EmitQuad(appearance.Texture, appearance.Tint);
                if (appearance.OverlayTexture >= 0)
                    EmitQuad(appearance.OverlayTexture, appearance.OverlayTint);

                void EmitQuad(int texture, int faceTint)
                {
                    var layer = Atlases.Terrain.LayerOfGridIndex(texture);
                    var color = TerrainLodMeshBuilder.PackTintedColor(faceTint, shade);
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

            ChunkLightVertex SampleLight(
                Side side, int minimumBlockLight, float x, float sampleAtY, float z)
            {
                if (lighting is null) return fallbackLight;
                if (side == Side.East) x += 0.01f;
                else if (side == Side.West) x -= 0.01f;
                else if (side == Side.South) z += 0.01f;
                else z -= 0.01f;
                var levels = lighting.GetLightLevels(
                    (int)MathF.Floor(x), (int)MathF.Floor(sampleAtY),
                    (int)MathF.Floor(z), minimumBlockLight);
                return new ChunkLightVertex(
                    ChunkVertexHelper.ToQuarterLevels(levels.Sky),
                    ChunkVertexHelper.ToQuarterLevels(levels.Block));
            }
        }

        return new TerrainLodSeamMeshData([.. vertices], [.. lights]);
    }

    private static void ValidateAdjacency(
        TerrainLodBoundarySummary owner,
        TerrainLodBoundarySummary neighbor,
        Side ownerSide)
    {
        var adjacent = ownerSide switch
        {
            Side.East => neighbor.ChunkX == owner.ChunkX + 1 &&
                         neighbor.ChunkZ == owner.ChunkZ,
            Side.South => neighbor.ChunkX == owner.ChunkX &&
                          neighbor.ChunkZ == owner.ChunkZ + 1,
            _ => throw new ArgumentOutOfRangeException(nameof(ownerSide), ownerSide,
                "Canonical terrain LOD seams are owned only on east or south boundaries.")
        };
        if (!adjacent)
            throw new ArgumentException(
                $"Terrain LOD seam columns are not adjacent: owner {owner.ChunkX},{owner.ChunkZ}, " +
                $"neighbor {neighbor.ChunkX},{neighbor.ChunkZ}, side {ownerSide}.");
    }
}
