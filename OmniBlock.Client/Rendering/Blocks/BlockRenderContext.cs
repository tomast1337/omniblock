using System.Runtime.CompilerServices;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Blocks;

public ref struct BlockRenderContext
{
    public readonly IBlockReader BlockReader;
    public readonly ILightProvider Lighting;
    public readonly Tessellator Tess;

    public int OverrideTexture;
    public readonly bool RenderAllFaces;
    public bool FlipTexture;
    public Box? OverrideBounds;
    public bool EnableAo = true;
    public int AoBlendMode = 0;

    public int UvRotateTop;
    public int UvRotateBottom;
    public int UvRotateNorth;
    public int UvRotateSouth;
    public int UvRotateEast;
    public int UvRotateWest;

    public int FlipTop;
    public int FlipBottom;
    public int FlipNorth;
    public int FlipSouth;
    public int FlipEast;
    public int FlipWest;

    // Custom flag for Pistons (Expanded/Short arm)
    public bool CustomFlag;

    public BlockRenderContext(
        IBlockReader blockReader, Tessellator tess,
        ILightProvider lighting,
        int overrideTexture = -1, bool renderAllFaces = false,
        bool flipTexture = false, Box? bounds = null,
        int uvTop = 0, int uvBottom = 0,
        int uvNorth = 0, int uvSouth = 0,
        int uvEast = 0, int uvWest = 0,
        int flipTop = 0, int flipBottom = 0,
        int flipNorth = 0, int flipSouth = 0,
        int flipEast = 0, int flipWest = 0,
        bool customFlag = false, bool enableAo = true,
        int aoBlendMode = 0)
    {
        BlockReader = blockReader;
        Tess = tess;
        Lighting = lighting;

        OverrideTexture = overrideTexture;
        RenderAllFaces = renderAllFaces;
        FlipTexture = flipTexture;
        OverrideBounds = bounds;

        UvRotateTop = uvTop;
        UvRotateBottom = uvBottom;
        UvRotateNorth = uvNorth;
        UvRotateSouth = uvSouth;
        UvRotateEast = uvEast;
        UvRotateWest = uvWest;

        FlipTop = flipTop;
        FlipBottom = flipBottom;
        FlipNorth = flipNorth;
        FlipSouth = flipSouth;
        FlipEast = flipEast;
        FlipWest = flipWest;

        AoBlendMode = aoBlendMode;
        EnableAo = enableAo;

        CustomFlag = customFlag;
    }

    /// <summary>
    ///     Grass's side is drawn twice: the bare dirt-and-grass tile, then a biome-tinted mask over
    ///     it. Matched by texture rather than by block, so anything drawing that side gets the
    ///     overlay -- which is how snowy and unsnowy grass share one renderer.
    /// </summary>
    private static readonly int s_grassSideTexture = Atlases.Terrain.IndexOf("omniblock:grass_block_side");
    private static readonly int s_grassSideOverlayTexture = Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ApplyVariance(int hash, TextureVariance variance, out int flipMask)
    {
        byte allowed = (byte)variance;
        flipMask = (hash & allowed & 12) >> 2;
        return hash & allowed & 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetTextureVarianceHash(int x, int y, int z)
    {
        unchecked
        {
            long seed = (x * 3129871L) ^ (z * 116129781L) ^ y;
            seed = (seed * seed * 42317861L) + (seed * 11L);
            return (int)(seed >> 16);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

    internal readonly void DrawBottomFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinX = (float)bb.MinX;
        float bbMaxX = (float)bb.MaxX;
        float bbMinZ = (float)bb.MinZ;
        float bbMaxZ = (float)bb.MaxZ;

        float bMinX = Clamp(bbMinX);
        float bMaxX = Clamp(bbMaxX);
        float bMinZ = Clamp(bbMinZ);
        float bMaxZ = Clamp(bbMaxZ);

        CalculateUv(bMinX, bMaxZ, UvRotateBottom, FlipBottom, out float u0, out float v0);
        CalculateUv(bMinX, bMinZ, UvRotateBottom, FlipBottom, out float u1, out float v1);
        CalculateUv(bMaxX, bMinZ, UvRotateBottom, FlipBottom, out float u2, out float v2);
        CalculateUv(bMaxX, bMaxZ, UvRotateBottom, FlipBottom, out float u3, out float v3);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float minX = pX + bbMinX;
        float maxX = pX + bbMaxX;
        float minY = pY + (float)bb.MinY;
        float minZ = pZ + bbMinZ;
        float maxZ = pZ + bbMaxZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, u1, v1);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(maxX, minY, minZ, u2, v2);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(maxX, minY, maxZ, u3, v3);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, u0, v0);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, u0, v0);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, u1, v1);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(maxX, minY, minZ, u2, v2);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(maxX, minY, maxZ, u3, v3);
            }
        }
        else
        {
            Tess.addVertexWithUV(minX, minY, maxZ, u0, v0);
            Tess.addVertexWithUV(minX, minY, minZ, u1, v1);
            Tess.addVertexWithUV(maxX, minY, minZ, u2, v2);
            Tess.addVertexWithUV(maxX, minY, maxZ, u3, v3);
        }
    }

    internal readonly void DrawTopFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinX = (float)bb.MinX;
        float bbMaxX = (float)bb.MaxX;
        float bbMinZ = (float)bb.MinZ;
        float bbMaxZ = (float)bb.MaxZ;

        float bMinX = Clamp(bbMinX);
        float bMaxX = Clamp(bbMaxX);
        float bMinZ = Clamp(bbMinZ);
        float bMaxZ = Clamp(bbMaxZ);

        CalculateUv(bMaxX, bMaxZ, UvRotateTop, FlipTop, out float u0, out float v0);
        CalculateUv(bMaxX, bMinZ, UvRotateTop, FlipTop, out float u1, out float v1);
        CalculateUv(bMinX, bMinZ, UvRotateTop, FlipTop, out float u2, out float v2);
        CalculateUv(bMinX, bMaxZ, UvRotateTop, FlipTop, out float u3, out float v3);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float minX = pX + bbMinX;
        float maxX = pX + bbMaxX;
        float maxY = pY + (float)bb.MaxY;
        float minZ = pZ + bbMinZ;
        float maxZ = pZ + bbMaxZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, minZ, u1, v1);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, u2, v2);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, u3, v3);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, maxZ, u0, v0);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, maxZ, u0, v0);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, minZ, u1, v1);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, u2, v2);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, u3, v3);
            }
        }
        else
        {
            Tess.addVertexWithUV(maxX, maxY, maxZ, u0, v0);
            Tess.addVertexWithUV(maxX, maxY, minZ, u1, v1);
            Tess.addVertexWithUV(minX, maxY, minZ, u2, v2);
            Tess.addVertexWithUV(minX, maxY, maxZ, u3, v3);
        }
    }

    internal readonly void DrawNorthFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinY = (float)bb.MinY;
        float bbMaxY = (float)bb.MaxY;
        float bbMinZ = (float)bb.MinZ;
        float bbMaxZ = (float)bb.MaxZ;

        CalculateUv(bbMinZ, 1.0f - bbMaxY, UvRotateNorth, FlipNorth, out float uTl, out float vTl);
        CalculateUv(bbMinZ, 1.0f - bbMinY, UvRotateNorth, FlipNorth, out float uBl, out float vBl);
        CalculateUv(bbMaxZ, 1.0f - bbMinY, UvRotateNorth, FlipNorth, out float uBr, out float vBr);
        CalculateUv(bbMaxZ, 1.0f - bbMaxY, UvRotateNorth, FlipNorth, out float uTr, out float vTr);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float minX = pX + (float)bb.MinX;
        float minY = pY + bbMinY;
        float maxY = pY + bbMaxY;
        float minZ = pZ + bbMinZ;
        float maxZ = pZ + bbMaxZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, uTr, vTr);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, uTl, vTl);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, uTl, vTl);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, uTr, vTr);
            }
        }
        else
        {
            Tess.addVertexWithUV(minX, maxY, minZ, uTl, vTl);
            Tess.addVertexWithUV(minX, minY, minZ, uBl, vBl);
            Tess.addVertexWithUV(minX, minY, maxZ, uBr, vBr);
            Tess.addVertexWithUV(minX, maxY, maxZ, uTr, vTr);
        }
    }

    internal readonly void DrawSouthFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinY = (float)bb.MinY;
        float bbMaxY = (float)bb.MaxY;
        float bbMinZ = (float)bb.MinZ;
        float bbMaxZ = (float)bb.MaxZ;

        float bMinY = Clamp(bbMinY);
        float bMaxY = Clamp(bbMaxY);
        float bMinZ = Clamp(bbMinZ);
        float bMaxZ = Clamp(bbMaxZ);

        CalculateUv(1.0f - bMaxZ, 1.0f - bMaxY, UvRotateSouth, FlipSouth, out float uTl, out float vTl);
        CalculateUv(1.0f - bMaxZ, 1.0f - bMinY, UvRotateSouth, FlipSouth, out float uBl, out float vBl);
        CalculateUv(1.0f - bMinZ, 1.0f - bMinY, UvRotateSouth, FlipSouth, out float uBr, out float vBr);
        CalculateUv(1.0f - bMinZ, 1.0f - bMaxY, UvRotateSouth, FlipSouth, out float uTr, out float vTr);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float posX = pX + (float)bb.MaxX;
        float minY = pY + bbMinY;
        float maxY = pY + bbMaxY;
        float minZ = pZ + bbMinZ;
        float maxZ = pZ + bbMaxZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(posX, minY, maxZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(posX, minY, minZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(posX, maxY, minZ, uTr, vTr);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(posX, maxY, maxZ, uTl, vTl);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(posX, maxY, maxZ, uTl, vTl);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(posX, minY, maxZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(posX, minY, minZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(posX, maxY, minZ, uTr, vTr);
            }
        }
        else
        {
            Tess.addVertexWithUV(posX, maxY, maxZ, uTl, vTl);
            Tess.addVertexWithUV(posX, minY, maxZ, uBl, vBl);
            Tess.addVertexWithUV(posX, minY, minZ, uBr, vBr);
            Tess.addVertexWithUV(posX, maxY, minZ, uTr, vTr);
        }
    }

    internal readonly void DrawEastFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinX = (float)bb.MinX;
        float bbMaxX = (float)bb.MaxX;
        float bbMinY = (float)bb.MinY;
        float bbMaxY = (float)bb.MaxY;

        float bMinX = Clamp(bbMinX);
        float bMaxX = Clamp(bbMaxX);
        float bMinY = Clamp(bbMinY);
        float bMaxY = Clamp(bbMaxY);

        CalculateUv(1.0f - bMaxX, 1.0f - bMaxY, UvRotateEast, FlipEast, out float uTl, out float vTl);
        CalculateUv(1.0f - bMaxX, 1.0f - bMinY, UvRotateEast, FlipEast, out float uBl, out float vBl);
        CalculateUv(1.0f - bMinX, 1.0f - bMinY, UvRotateEast, FlipEast, out float uBr, out float vBr);
        CalculateUv(1.0f - bMinX, 1.0f - bMaxY, UvRotateEast, FlipEast, out float uTr, out float vTr);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float minX = pX + bbMinX;
        float maxX = pX + bbMaxX;
        float minY = pY + bbMinY;
        float maxY = pY + bbMaxY;
        float minZ = pZ + (float)bb.MinZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(maxX, minY, minZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, uTr, vTr);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, minZ, uTl, vTl);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(maxX, maxY, minZ, uTl, vTl);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(maxX, minY, minZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(minX, minY, minZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(minX, maxY, minZ, uTr, vTr);
            }
        }
        else
        {
            Tess.addVertexWithUV(maxX, maxY, minZ, uTl, vTl);
            Tess.addVertexWithUV(maxX, minY, minZ, uBl, vBl);
            Tess.addVertexWithUV(minX, minY, minZ, uBr, vBr);
            Tess.addVertexWithUV(minX, maxY, minZ, uTr, vTr);
        }
    }

    internal readonly void DrawWestFace(Block block, in Vec3D pos, in FaceColors colors, int textureId, bool flipped = false)
    {
        Box bb = OverrideBounds ?? block.BoundingBox;
        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        float bbMinX = (float)bb.MinX;
        float bbMaxX = (float)bb.MaxX;
        float bbMinY = (float)bb.MinY;
        float bbMaxY = (float)bb.MaxY;

        float bMinX = Clamp(bbMinX);
        float bMaxX = Clamp(bbMaxX);
        float bMinY = Clamp(bbMinY);
        float bMaxY = Clamp(bbMaxY);

        CalculateUv(bMinX, 1.0f - bMaxY, UvRotateWest, FlipWest, out float uTl, out float vTl);
        CalculateUv(bMinX, 1.0f - bMinY, UvRotateWest, FlipWest, out float uBl, out float vBl);
        CalculateUv(bMaxX, 1.0f - bMinY, UvRotateWest, FlipWest, out float uBr, out float vBr);
        CalculateUv(bMaxX, 1.0f - bMaxY, UvRotateWest, FlipWest, out float uTr, out float vTr);

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float minX = pX + bbMinX;
        float maxX = pX + bbMaxX;
        float minY = pY + bbMinY;
        float maxY = pY + bbMaxY;
        float maxZ = pZ + (float)bb.MaxZ;

        if (EnableAo)
        {
            if (flipped)
            {
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(maxX, minY, maxZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(maxX, maxY, maxZ, uTr, vTr);
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, uTl, vTl);
            }
            else
            {
                colors.ApplyTopLeft(Tess);
                Tess.addVertexWithUV(minX, maxY, maxZ, uTl, vTl);
                colors.ApplyBottomLeft(Tess);
                Tess.addVertexWithUV(minX, minY, maxZ, uBl, vBl);
                colors.ApplyBottomRight(Tess);
                Tess.addVertexWithUV(maxX, minY, maxZ, uBr, vBr);
                colors.ApplyTopRight(Tess);
                Tess.addVertexWithUV(maxX, maxY, maxZ, uTr, vTr);
            }
        }
        else
        {
            Tess.addVertexWithUV(minX, maxY, maxZ, uTl, vTl);
            Tess.addVertexWithUV(minX, minY, maxZ, uBl, vBl);
            Tess.addVertexWithUV(maxX, minY, maxZ, uBr, vBr);
            Tess.addVertexWithUV(maxX, maxY, maxZ, uTr, vTr);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool IsOpaque(int x, int y, int z) => !Block.BlocksAllowVision[BlockReader.GetBlockId(x, y, z)];

    /// <summary>
    ///     Sets the light the next vertices carry from one cell, for a primitive lit as a whole.
    /// </summary>
    /// <remarks>
    ///     The sub-renderers draw shapes that are not block faces — a torch, a rail, a wire — and
    ///     never had per-corner lighting. They used to fold the one luminance into the colour they
    ///     set; now they set the colour and this sets the light.
    /// </remarks>
    internal readonly void SetLightAt(in Block block, int x, int y, int z)
    {
        LightLevels levels = block.GetLightLevels(Lighting, x, y, z);
        Tess.setLight(levels.Sky, levels.Block);
    }

    /// <summary>Sets the light for something that should come out at full brightness regardless.</summary>
    /// <remarks>
    ///     Through the block channel rather than the sky channel, so it stays bright after dark.
    ///     This is what the torch and the repeater's torch used to get by forcing their luminance to
    ///     one before multiplying it into the colour.
    /// </remarks>
    internal readonly void SetFullBright() => Tess.setLight(0.0f, 15.0f);

    /// <summary>
    ///     Sets one light for a face whose context does not read the per-corner values.
    /// </summary>
    /// <remarks>
    ///     With <see cref="EnableAo" /> off — inventory blocks, stairs, fences, piston bases — the
    ///     draw methods take the colour the caller set and ignore the <see cref="FaceColors" />
    ///     passed to them, so the light in it would never reach a vertex. It has to be set here
    ///     instead, or the face keeps whatever the previous block left behind.
    /// </remarks>
    private readonly void ApplyFlatLightIfUnread(CornerLight light)
    {
        if (!EnableAo)
        {
            Tess.setLight(light.Sky, light.Block);
        }
    }

    private readonly CornerLight Sample(in Block block, int x, int y, int z)
    {
        LightLevels levels = block.GetLightLevels(Lighting, x, y, z);
        return new CornerLight(levels.Sky, levels.Block);
    }

    /// <summary>
    ///     The four corner light values for a face, each the mean of the four cells meeting there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         All six faces sample the same nine cells in the plane one step along
    ///         <paramref name="ox" />/<paramref name="oy" />/<paramref name="oz" />: the centre, the
    ///         four edge neighbours along the in-plane axes <c>a</c> and <c>b</c>, and the four
    ///         corners. A corner whose two adjacent edge cells are both opaque cannot be seen from
    ///         this face, so it reads as the <c>a</c> edge instead — that is what keeps an inside
    ///         corner from picking up light leaking around a solid block.
    ///     </para>
    ///     <para>
    ///         The quadrants come back in a fixed order and each face maps them onto its own winding.
    ///         They used to be written out per face, six times, which is why the mapping is the one
    ///         thing here worth checking against the old code rather than reading forwards.
    ///     </para>
    /// </remarks>
    private readonly FaceQuadrants SampleFace(
        in Block block, in BlockPos pos,
        int ox, int oy, int oz,
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        int cx = pos.X + ox, cy = pos.Y + oy, cz = pos.Z + oz;

        CornerLight centre = Sample(block, cx, cy, cz);
        CornerLight edgeAMinus = Sample(block, cx - ax, cy - ay, cz - az);
        CornerLight edgeAPlus = Sample(block, cx + ax, cy + ay, cz + az);
        CornerLight edgeBMinus = Sample(block, cx - bx, cy - by, cz - bz);
        CornerLight edgeBPlus = Sample(block, cx + bx, cy + by, cz + bz);

        bool opaqueAMinus = IsOpaque(cx - ax, cy - ay, cz - az);
        bool opaqueAPlus = IsOpaque(cx + ax, cy + ay, cz + az);
        bool opaqueBMinus = IsOpaque(cx - bx, cy - by, cz - bz);
        bool opaqueBPlus = IsOpaque(cx + bx, cy + by, cz + bz);

        CornerLight cornerMinusMinus = opaqueAMinus && opaqueBMinus
            ? edgeAMinus
            : Sample(block, cx - ax - bx, cy - ay - by, cz - az - bz);

        CornerLight cornerMinusPlus = opaqueAMinus && opaqueBPlus
            ? edgeAMinus
            : Sample(block, cx - ax + bx, cy - ay + by, cz - az + bz);

        CornerLight cornerPlusMinus = opaqueAPlus && opaqueBMinus
            ? edgeAPlus
            : Sample(block, cx + ax - bx, cy + ay - by, cz + az - bz);

        CornerLight cornerPlusPlus = opaqueAPlus && opaqueBPlus
            ? edgeAPlus
            : Sample(block, cx + ax + bx, cy + ay + by, cz + az + bz);

        return new FaceQuadrants(
            CornerLight.Mean(cornerMinusMinus, edgeAMinus, edgeBMinus, centre),
            CornerLight.Mean(cornerMinusPlus, edgeAMinus, edgeBPlus, centre),
            CornerLight.Mean(cornerPlusMinus, edgeAPlus, edgeBMinus, centre),
            CornerLight.Mean(cornerPlusPlus, edgeAPlus, edgeBPlus, centre));
    }

    /// <summary>The four quadrant means of a face, named by their sign along the two in-plane axes.</summary>
    private readonly ref struct FaceQuadrants(CornerLight mm, CornerLight mp, CornerLight pm, CornerLight pp)
    {
        public readonly CornerLight MinusMinus = mm;
        public readonly CornerLight MinusPlus = mp;
        public readonly CornerLight PlusMinus = pm;
        public readonly CornerLight PlusPlus = pp;
    }

    internal readonly bool DrawBlock(in Block block, in BlockPos pos)
    {
        bool hasRendered = false;
        Box bounds = OverrideBounds ?? block.BoundingBox;

        int colorMultiplier = block.GetColorMultiplier(BlockReader, pos.X, pos.Y, pos.Z);
        float r = (colorMultiplier >> 16 & 255) * 0.0039215686F;
        float g = (colorMultiplier >> 8 & 255) * 0.0039215686F;
        float b = (colorMultiplier & 255) * 0.0039215686F;

        bool hasOverrideTex = OverrideTexture >= 0;
        bool tintBottom = true, tintTop = true, tintEast = true, tintWest = true, tintNorth = true, tintSouth = true;

        if (block.TextureId == 3 || hasOverrideTex)
        {
            tintBottom = tintEast = tintWest = tintNorth = tintSouth = false;
        }

        CornerLight v0, v1, v2, v3;
        bool ao = AoBlendMode > 0;
        Vec3D vecPos = new(pos.X, pos.Y, pos.Z); // Allocate struct once

        // BOTTOM FACE (Y - 1)
        if (RenderAllFaces || bounds.MinY > 0.0F || block.IsSideVisible(BlockReader, pos.X, pos.Y - 1, pos.Z, Side.Down))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X, pos.Y - 1, pos.Z); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, 0, -1, 0, 1, 0, 0, 0, 0, 1);
                v0 = q.MinusPlus;
                v1 = q.MinusMinus;
                v2 = q.PlusMinus;
                v3 = q.PlusPlus;
            }

            ApplyFlatLightIfUnread(v0);
            var colors = FaceColors.AssignVertexColors(v0, v1, v2, v3, r, g, b, 0.5F, tintBottom);
            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, Side.Down);

            DrawBottomFace(block, in vecPos, colors, textureId, ao && (v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight));

            hasRendered = true;
        }

        // TOP FACE (Y + 1)
        if (RenderAllFaces || bounds.MaxY < 1.0F || block.IsSideVisible(BlockReader, pos.X, pos.Y + 1, pos.Z, Side.Up))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X, pos.Y + 1, pos.Z); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, 0, 1, 0, 1, 0, 0, 0, 0, 1);
                v0 = q.PlusPlus;
                v1 = q.PlusMinus;
                v2 = q.MinusMinus;
                v3 = q.MinusPlus;
            }

            ApplyFlatLightIfUnread(v0);
            var colors = FaceColors.AssignVertexColors(v0, v1, v2, v3, r, g, b, 1.0F, tintTop);
            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, Side.Up);

            DrawTopFace(block, in vecPos, colors, textureId, ao && (v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight));

            hasRendered = true;
        }

        // EAST FACE (Z - 1)
        if (RenderAllFaces || bounds.MinZ > 0.0F || block.IsSideVisible(BlockReader, pos.X, pos.Y, pos.Z - 1, Side.North))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X, pos.Y, pos.Z - 1); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, 0, 0, -1, 1, 0, 0, 0, 1, 0);
                v0 = q.MinusPlus;
                v1 = q.PlusPlus;
                v2 = q.PlusMinus;
                v3 = q.MinusMinus;
            }

            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, Side.North);
            ApplyFlatLightIfUnread(v1);
            var colors = FaceColors.AssignVertexColors(v1, v2, v3, v0, r, g, b, 0.8F, tintEast);
            bool flipped = ao && (v1.FlipWeight + v3.FlipWeight > v2.FlipWeight + v0.FlipWeight);

            DrawEastFace(block, in vecPos, colors, textureId, flipped);

            if (textureId == s_grassSideTexture && !hasOverrideTex)
            {
                var overlayColors = FaceColors.AssignVertexColors(v1, v2, v3, v0, r, g, b, 0.8F, true);
                DrawEastFace(block, in vecPos, overlayColors, s_grassSideOverlayTexture, flipped);
            }

            hasRendered = true;
        }

        // WEST FACE (Z + 1)
        if (RenderAllFaces || bounds.MaxZ < 1.0F || block.IsSideVisible(BlockReader, pos.X, pos.Y, pos.Z + 1, Side.South))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X, pos.Y, pos.Z + 1); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, 0, 0, 1, 1, 0, 0, 0, 1, 0);
                v0 = q.MinusPlus;
                v1 = q.MinusMinus;
                v2 = q.PlusMinus;
                v3 = q.PlusPlus;
            }

            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, Side.South);
            ApplyFlatLightIfUnread(v0);
            var colors = FaceColors.AssignVertexColors(v0, v1, v2, v3, r, g, b, 0.8F, tintWest);
            bool flipped = ao && (v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight);

            DrawWestFace(block, in vecPos, colors, textureId, flipped);

            if (textureId == s_grassSideTexture && !hasOverrideTex)
            {
                var overlayColors = FaceColors.AssignVertexColors(v0, v1, v2, v3, r, g, b, 0.8F, true);
                DrawWestFace(block, in vecPos, overlayColors, s_grassSideOverlayTexture, flipped);
            }

            hasRendered = true;
        }

        // NORTH FACE (X - 1)
        if (RenderAllFaces || bounds.MinX > 0.0F || block.IsSideVisible(BlockReader, pos.X - 1, pos.Y, pos.Z, Side.West))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X - 1, pos.Y, pos.Z); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, -1, 0, 0, 0, 0, 1, 0, 1, 0);
                v0 = q.PlusPlus;
                v1 = q.MinusPlus;
                v2 = q.MinusMinus;
                v3 = q.PlusMinus;
            }

            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, Side.West);
            ApplyFlatLightIfUnread(v1);
            var colors = FaceColors.AssignVertexColors(v1, v2, v3, v0, r, g, b, 0.6F, tintNorth);
            bool flipped = ao && (v1.FlipWeight + v3.FlipWeight > v2.FlipWeight + v0.FlipWeight);

            DrawNorthFace(block, in vecPos, colors, textureId, flipped);

            if (textureId == s_grassSideTexture && !hasOverrideTex)
            {
                var overlayColors = FaceColors.AssignVertexColors(v1, v2, v3, v0, r, g, b, 0.6F, true);
                DrawNorthFace(block, in vecPos, overlayColors, s_grassSideOverlayTexture, flipped);
            }

            hasRendered = true;
        }

        // SOUTH FACE (X + 1)
        if (RenderAllFaces || bounds.MaxX < 1.0F || block.IsSideVisible(BlockReader, pos.X + 1, pos.Y, pos.Z, Side.East))
        {
            if (!ao) { v0 = v1 = v2 = v3 = Sample(block, pos.X + 1, pos.Y, pos.Z); }
            else
            {
                FaceQuadrants q = SampleFace(block, pos, 1, 0, 0, 0, 0, 1, 0, 1, 0);
                v0 = q.PlusMinus;
                v1 = q.MinusMinus;
                v2 = q.MinusPlus;
                v3 = q.PlusPlus;
            }

            int textureId = hasOverrideTex ? OverrideTexture : block.GetTextureId(BlockReader, pos.X, pos.Y, pos.Z, 5.ToSide());
            ApplyFlatLightIfUnread(v3);
            var colors = FaceColors.AssignVertexColors(v3, v0, v1, v2, r, g, b, 0.6F, tintSouth);
            bool flipped = ao && (v3.FlipWeight + v1.FlipWeight > v0.FlipWeight + v2.FlipWeight);

            DrawSouthFace(block, in vecPos, colors, textureId, flipped);

            if (textureId == s_grassSideTexture && !hasOverrideTex)
            {
                var overlayColors = FaceColors.AssignVertexColors(v3, v0, v1, v2, r, g, b, 0.6F, true);
                DrawSouthFace(block, in vecPos, overlayColors, s_grassSideOverlayTexture, flipped);
            }

            hasRendered = true;
        }

        return hasRendered;
    }

    /// <summary>
    ///     The four corner light values and diagonal-flip decision for one face of a full 1x1x1
    ///     cube, computed exactly as <see cref="DrawBlock" />'s own AO branch does for that face —
    ///     duplicated rather than extracted so the battle-tested per-block path stays untouched.
    /// </summary>
    /// <remarks>
    ///     Used by the chunk mesher's greedy-meshing fast path, which needs this data before it
    ///     knows whether a face will end up merged with its neighbours and therefore cannot go
    ///     through <see cref="DrawBlock" />'s immediate-emit flow.
    /// </remarks>
    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeTopFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, 0, 1, 0, 1, 0, 0, 0, 0, 1);
        CornerLight v0 = q.PlusPlus, v1 = q.PlusMinus, v2 = q.MinusMinus, v3 = q.MinusPlus;
        return (v0, v1, v2, v3, v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight);
    }

    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeBottomFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, 0, -1, 0, 1, 0, 0, 0, 0, 1);
        CornerLight v0 = q.MinusPlus, v1 = q.MinusMinus, v2 = q.PlusMinus, v3 = q.PlusPlus;
        return (v0, v1, v2, v3, v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight);
    }

    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeEastFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, 0, 0, -1, 1, 0, 0, 0, 1, 0);
        CornerLight v0 = q.MinusPlus, v1 = q.PlusPlus, v2 = q.PlusMinus, v3 = q.MinusMinus;
        return (v0, v1, v2, v3, v1.FlipWeight + v3.FlipWeight > v2.FlipWeight + v0.FlipWeight);
    }

    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeWestFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, 0, 0, 1, 1, 0, 0, 0, 1, 0);
        CornerLight v0 = q.MinusPlus, v1 = q.MinusMinus, v2 = q.PlusMinus, v3 = q.PlusPlus;
        return (v0, v1, v2, v3, v0.FlipWeight + v2.FlipWeight > v1.FlipWeight + v3.FlipWeight);
    }

    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeNorthFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, -1, 0, 0, 0, 0, 1, 0, 1, 0);
        CornerLight v0 = q.PlusPlus, v1 = q.MinusPlus, v2 = q.MinusMinus, v3 = q.PlusMinus;
        return (v0, v1, v2, v3, v1.FlipWeight + v3.FlipWeight > v2.FlipWeight + v0.FlipWeight);
    }

    internal readonly (CornerLight V0, CornerLight V1, CornerLight V2, CornerLight V3, bool Flipped) ComputeSouthFaceLight(in Block block, in BlockPos pos)
    {
        FaceQuadrants q = SampleFace(block, pos, 1, 0, 0, 0, 0, 1, 0, 1, 0);
        CornerLight v0 = q.PlusMinus, v1 = q.MinusMinus, v2 = q.MinusPlus, v3 = q.PlusPlus;
        return (v0, v1, v2, v3, v3.FlipWeight + v1.FlipWeight > v0.FlipWeight + v2.FlipWeight);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly void DrawTorch(in Block block, in Vec3D pos, float tiltX, float tiltZ)
    {
        // A sixteenth of the tile, whatever resolution the tile actually is: the torch's flame sits
        // at fixed pixel offsets in the vanilla 16x16 art, and those are the same fractions of a
        // 128x128 one a pack might ship instead.
        const float texel = 1.0f / 16.0f;
        const float radius = 1.0f / 16.0f;
        const float height = 10.0f / 16.0f;
        const float tipOffsetBase = 1.0f - height;

        const float topMinUOffset = 7.0f * texel;
        const float topMaxUOffset = 9.0f * texel;
        const float topMinVOffset = 6.0f * texel;
        const float topMaxVOffset = 8.0f * texel;

        int textureId = OverrideTexture >= 0 ? OverrideTexture : block.GetTexture(0);

        Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        const float minU = 0.0f;
        const float maxU = 1.0f;
        const float minV = 0.0f;
        const float maxV = 1.0f;

        const float topMinU = minU + topMinUOffset;
        const float topMinV = minV + topMinVOffset;
        const float topMaxU = minU + topMaxUOffset;
        const float topMaxV = minV + topMaxVOffset;

        float pX = (float)pos.X;
        float pY = (float)pos.Y;
        float pZ = (float)pos.Z;

        float centerX = pX + 0.5f;
        float centerZ = pZ + 0.5f;
        float leftX = pX;
        float rightX = pX + 1.0f;
        float frontZ = pZ;
        float backZ = pZ + 1.0f;

        float yBot = pY;
        float yTop = pY + 1.0f;
        float yTip = pY + height;

        float cXmin = centerX - radius;
        float cXmax = centerX + radius;
        float cZmin = centerZ - radius;
        float cZmax = centerZ + radius;

        float tLeftX = leftX + tiltX;
        float tRightX = rightX + tiltX;
        float tFrontZ = frontZ + tiltZ;
        float tBackZ = backZ + tiltZ;

        float cXminT = cXmin + tiltX;
        float cXmaxT = cXmax + tiltX;
        float cZminT = cZmin + tiltZ;
        float cZmaxT = cZmax + tiltZ;

        float tipX = centerX + tiltX * tipOffsetBase;
        float tipZ = centerZ + tiltZ * tipOffsetBase;

        Tess.setColorOpaque_F(1.0f, 1.0f, 1.0f);

        // TOP FACE
        Tess.addVertexWithUV(tipX - radius, yTip, tipZ - radius, topMinU, topMinV);
        Tess.addVertexWithUV(tipX - radius, yTip, tipZ + radius, topMinU, topMaxV);
        Tess.addVertexWithUV(tipX + radius, yTip, tipZ + radius, topMaxU, topMaxV);
        Tess.addVertexWithUV(tipX + radius, yTip, tipZ - radius, topMaxU, topMinV);

        // West Face
        Tess.addVertexWithUV(cXmin, yTop, frontZ, minU, minV);
        Tess.addVertexWithUV(cXminT, yBot, tFrontZ, minU, maxV);
        Tess.addVertexWithUV(cXminT, yBot, tBackZ, maxU, maxV);
        Tess.addVertexWithUV(cXmin, yTop, backZ, maxU, minV);

        // East Face
        Tess.addVertexWithUV(cXmax, yTop, backZ, minU, minV);
        Tess.addVertexWithUV(cXmaxT, yBot, tBackZ, minU, maxV);
        Tess.addVertexWithUV(cXmaxT, yBot, tFrontZ, maxU, maxV);
        Tess.addVertexWithUV(cXmax, yTop, frontZ, maxU, minV);

        // North Face
        Tess.addVertexWithUV(leftX, yTop, cZmax, minU, minV);
        Tess.addVertexWithUV(tLeftX, yBot, cZmaxT, minU, maxV);
        Tess.addVertexWithUV(tRightX, yBot, cZmaxT, maxU, maxV);
        Tess.addVertexWithUV(rightX, yTop, cZmax, maxU, minV);

        // South Face
        Tess.addVertexWithUV(rightX, yTop, cZmin, minU, minV);
        Tess.addVertexWithUV(tRightX, yBot, cZminT, minU, maxV);
        Tess.addVertexWithUV(tLeftX, yBot, cZminT, maxU, maxV);
        Tess.addVertexWithUV(leftX, yTop, cZmin, maxU, minV);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CalculateUv(float h, float v, int rotation, int flipMask, out float u, out float outV)
    {
        if (rotation == 0 && !FlipTexture && flipMask == 0)
        {
            u = h;
            outV = v;
            return;
        }

        float fU, fV;

        switch (rotation)
        {
            case 1:
                fU = v;
                fV = 1.0f - h;
                break;
            case 2:
                fU = 1.0f - h;
                fV = 1.0f - v;
                break;
            case 3:
                fU = 1.0f - v;
                fV = h;
                break;
            case 4:
                fU = 1.0f - h;
                fV = v;
                break;
            case 5:
                fU = v;
                fV = h;
                break;
            case 6:
                fU = h;
                fV = 1.0f - v;
                break;
            case 7:
                fU = 1.0f - v;
                fV = 1.0f - h;
                break;
            default:
                fU = h;
                fV = v;
                break;
        }

        fU = FlipTexture ? 1.0f - fU : fU;

        if ((flipMask & 1) != 0) fU = 1.0f - fU;
        if ((flipMask & 2) != 0) fV = 1.0f - fV;

        u = fU;
        outV = fV;
    }
}
