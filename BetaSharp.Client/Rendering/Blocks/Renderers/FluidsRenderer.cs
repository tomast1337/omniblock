using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class FluidsRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        // Base fluid color tint (e.g., biome water color)
        int colorMultiplier = block.GetColorMultiplier(ctx.BlockReader, pos.X, pos.Y, pos.Z);
        float tintR = (colorMultiplier >> 16 & 255) / 255.0F;
        float tintG = (colorMultiplier >> 8 & 255) / 255.0F;
        float tintB = (colorMultiplier & 255) / 255.0F;

        // Determine which faces are actually visible to the player
        bool isTopVisible = block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y + 1, pos.Z, Side.Up);
        bool isBottomVisible = block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y - 1, pos.Z, 0);
        bool[] sideVisible =
        [
            block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z - 1, Side.North),
            block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z + 1, Side.South),
            block.IsSideVisible(ctx.BlockReader, pos.X - 1, pos.Y, pos.Z, Side.West),
            block.IsSideVisible(ctx.BlockReader, pos.X + 1, pos.Y, pos.Z, Side.East)
        ];

        // Fast exit if completely surrounded
        if (!isTopVisible && !isBottomVisible && !sideVisible[0] && !sideVisible[1] && !sideVisible[2] &&
            !sideVisible[3])
        {
            return false;
        }

        bool hasRendered = false;

        // Directional shading
        const float lightBottom = 0.5F;
        const float lightTop = 1.0F;
        const float lightZ = 0.8F; // North/South
        const float lightX = 0.6F; // East/West

        Material material = block.Material;
        int meta = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);

        // Calculate the height of the fluid at each of the 4 corners of this block
        float heightNw = GetFluidVertexHeight(ref ctx, pos.X, pos.Y, pos.Z, material);
        float heightSw = GetFluidVertexHeight(ref ctx, pos.X, pos.Y, pos.Z + 1, material);
        float heightSe = GetFluidVertexHeight(ref ctx, pos.X + 1, pos.Y, pos.Z + 1, material);
        float heightNe = GetFluidVertexHeight(ref ctx, pos.X + 1, pos.Y, pos.Z, material);

        // TOP FACE (Flowing Surface)
        if (ctx.RenderAllFaces || isTopVisible)
        {
            hasRendered = true;
            int textureId = block.GetTexture(Side.Up, meta);
            float flowAngle = (float)FluidMath.GetFlowingAngle(ctx.BlockReader, pos.X, pos.Y, pos.Z, material);

            // If flowing, switch to the flowing texture variant
            if (flowAngle > -999.0F)
            {
                textureId = block.GetTexture(Side.North, meta);
            }

            ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

            // Still water is a quad turned about the middle of its tile, so it lands back on the
            // tile exactly. Flowing water turns about the corner instead and sweeps up to 0.71 of a
            // tile past the edge, which the layer's wrap folds back onto itself -- Beta got the same
            // result by writing the flowing frame into a 2x2 block of atlas cells.
            float centerU = 0.5F;
            float centerV = 0.5F;

            if (flowAngle <= -999.0F)
            {
                flowAngle = 0.0F;
            }
            else
            {
                centerU = 1.0F;
                centerV = 1.0F;
            }

            // Calculate rotational offsets for the UVs to make the texture flow in the correct direction
            float sinAngle = MathHelper.Sin(flowAngle) * 0.5F;
            float cosAngle = MathHelper.Cos(flowAngle) * 0.5F;

            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            ctx.Tess.setColorOpaque_F(lightTop * tintR, lightTop * tintG, lightTop * tintB);

            // Draw top face with dynamic heights and rotated UVs
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y + heightNw, pos.Z + 0, centerU - cosAngle - sinAngle,
                centerV - cosAngle + sinAngle);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y + heightSw, pos.Z + 1, centerU - cosAngle + sinAngle,
                centerV + cosAngle + sinAngle);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + heightSe, pos.Z + 1, centerU + cosAngle + sinAngle,
                centerV + cosAngle - sinAngle);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + heightNe, pos.Z + 0, centerU + cosAngle - sinAngle,
                centerV - cosAngle - sinAngle);
        }

        // BOTTOM FACE
        if (ctx.RenderAllFaces || isBottomVisible)
        {
            ctx.SetLightAt(block, pos.X, pos.Y - 1, pos.Z);
            ctx.Tess.setColorOpaque_F(lightBottom, lightBottom, lightBottom);

            // Through a context with ambient occlusion off, because the colour and the light are set
            // above rather than carried per corner. Drawing it through ctx meant the empty FaceColors
            // below overwrote both with zero, which is why this face came out black.
            BlockRenderContext flatCtx = ctx with { EnableAo = false };
            int tex = block.GetTexture(0);

            // Note: Fluids don't override bounds for the bottom face, so we just pass the default context
            flatCtx.DrawBottomFace(block, new Vec3D(pos.X, pos.Y, pos.Z), default, tex);
            hasRendered = true;
        }

        // SIDE FACES (North, South, West, East)
        for (int side = 0; side < 4; ++side)
        {
            int adjX = pos.X;
            int adjZ = pos.Z;

            if (side == 0) adjZ = pos.Z - 1; // North
            if (side == 1) adjZ = pos.Z + 1; // South
            if (side == 2) adjX = pos.X - 1; // West
            if (side == 3) adjX = pos.X + 1; // East

            int textureId = block.GetTexture((side + 2).ToSide(), meta);
            ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

            if (ctx.RenderAllFaces || sideVisible[side])
            {
                float h1, h2; // Top corner heights for this face
                float x1, x2; // X coordinates
                float z1, z2; // Z coordinates

                if (side == 0) // North
                {
                    h1 = heightNw;
                    h2 = heightNe;
                    x1 = pos.X;
                    x2 = pos.X + 1;
                    z1 = pos.Z;
                    z2 = pos.Z;
                }
                else if (side == 1) // South
                {
                    h1 = heightSe;
                    h2 = heightSw;
                    x1 = pos.X + 1;
                    x2 = pos.X;
                    z1 = pos.Z + 1;
                    z2 = pos.Z + 1;
                }
                else if (side == 2) // West
                {
                    h1 = heightSw;
                    h2 = heightNw;
                    x1 = pos.X;
                    x2 = pos.X;
                    z1 = pos.Z + 1;
                    z2 = pos.Z;
                }
                else // East
                {
                    h1 = heightNe;
                    h2 = heightSe;
                    x1 = pos.X + 1;
                    x2 = pos.X + 1;
                    z1 = pos.Z;
                    z2 = pos.Z + 1;
                }

                hasRendered = true;

                // Crop the UVs vertically so the texture doesn't stretch on short flowing water blocks
                const float minU = 0.0F;
                const float maxU = 1.0F;
                float minV1 = 1.0F - h1; // UV height match for corner 1
                float minV2 = 1.0F - h2; // UV height match for corner 2
                const float maxV = 1.0F;

                ctx.SetLightAt(block, adjX, pos.Y, adjZ);
                float shadow = (side < 2) ? lightZ : lightX;

                ctx.Tess.setColorOpaque_F(lightTop * shadow * tintR, lightTop * shadow * tintG,
                    lightTop * shadow * tintB);

                // Draw the side face matching the sloped top corners
                ctx.Tess.addVertexWithUV(x1, pos.Y + h1, z1, minU, minV1);
                ctx.Tess.addVertexWithUV(x2, pos.Y + h2, z2, maxU, minV2);
                ctx.Tess.addVertexWithUV(x2, pos.Y + 0, z2, maxU, maxV);
                ctx.Tess.addVertexWithUV(x1, pos.Y + 0, z1, minU, maxV);
            }
        }

        return hasRendered;
    }

    // Passed ctx.World explicitly into this helper method
    private float GetFluidVertexHeight(ref BlockRenderContext ctx, int x, int y, int z, Material material)
    {
        int totalWeight = 0;
        float totalDepth = 0.0F;

        // Iterate through the 2x2 grid sharing this vertex: (x, z), (x-1, z), (x, z-1), (x-1, z-1)
        for (int i = 0; i < 4; ++i)
        {
            int checkX = x - (i & 1);
            int checkZ = z - (i >> 1 & 1);

            // If there is fluid directly above any of the 4 blocks, the corner must be completely full (height 1.0)
            if (ctx.BlockReader.GetMaterial(checkX, y + 1, checkZ) == material)
            {
                return 1.0F;
            }

            Material neighborMaterial = ctx.BlockReader.GetMaterial(checkX, y, checkZ);

            if (neighborMaterial != material)
            {
                // If the neighbor is air or a non-solid block, it contributes "full depth" (pulls the water level down to 0)
                if (!neighborMaterial.IsSolid)
                {
                    ++totalDepth;
                    ++totalWeight;
                }
            }
            else
            {
                int neighborMeta = ctx.BlockReader.GetBlockMeta(checkX, y, checkZ);
                float fluidDepth = FluidMath.GetFluidHeightFromMeta(neighborMeta);

                // Meta >= 8 (falling fluid) or Meta == 0 (source block)
                if (neighborMeta >= 8 || neighborMeta == 0)
                {
                    // Source blocks and falling columns get 10x the "weight" in the average,
                    // heavily anchoring the fluid corner to their height.
                    totalDepth += fluidDepth * 10.0F;
                    totalWeight += 10;
                }

                totalDepth += fluidDepth;
                ++totalWeight;
            }
        }

        // Depth is measured from the top down. Subtract from 1.0 to get height from bottom up.
        return 1.0F - totalDepth / totalWeight;
    }
}
