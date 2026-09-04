using OmniBlock.Blocks;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class FireRenderer : IBlockRenderer
{
    /// <summary>The other half of the crossed pair, which the fire animation writes into too.</summary>
    private static readonly int s_secondFrameLayer = Atlases.Terrain.LayerOf("omniblock:fire_layer_1");

    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var textureId = block.GetTexture(0);
        if (ctx.OverrideTexture >= 0) textureId = ctx.OverrideTexture;

        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        ctx.Tess.setColorOpaque_F(1.0F, 1.0F, 1.0F);

        // Fire is drawn as a crossed pair of quads alternating between two tiles. An override --
        // the block-breaking overlay -- has only the one texture to give, so both alternate to it.
        var firstFrame = Atlases.Terrain.LayerOfGridIndex(textureId);
        var secondFrame = ctx.OverrideTexture >= 0 ? firstFrame : s_secondFrameLayer;

        ctx.Tess.setArrayLayer(firstFrame);

        var minU = 0.0F;
        var maxU = 1.0F;
        const float minV = 0.0F;
        const float maxV = 1.0F;

        var fireHeight = 1.4F;

        // If not on a solid/flammable floor, render climbing flames on walls
        if (!ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y - 1, pos.Z) && !BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X, pos.Y - 1, pos.Z))
        {
            var sideInset = 0.2F;
            var yOffset = 1.0F / 16.0F;

            // Variation: Flip texture or use second fire frame based on position
            if (((pos.X + pos.Y + pos.Z) & 1) == 1)
            {
                ctx.Tess.setArrayLayer(secondFrame);
            }

            if (((pos.X / 2 + pos.Y / 2 + pos.Z / 2) & 1) == 1)
            {
                (minU, maxU) = (maxU, minU);
            }

            // Climbing West Wall
            if (BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X - 1, pos.Y, pos.Z))
            {
                ctx.Tess.addVertexWithUV(pos.X + sideInset, pos.Y + fireHeight + yOffset, pos.Z + 1, maxU, minV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + sideInset, pos.Y + fireHeight + yOffset, pos.Z, minU, minV);
                // Backface
                ctx.Tess.addVertexWithUV(pos.X + sideInset, pos.Y + fireHeight + yOffset, pos.Z, minU, minV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + sideInset, pos.Y + fireHeight + yOffset, pos.Z + 1, maxU, minV);
            }

            // Climbing East Wall
            if (BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X + 1, pos.Y, pos.Z))
            {
                ctx.Tess.addVertexWithUV(pos.X + 1 - sideInset, pos.Y + fireHeight + yOffset, pos.Z, minU, minV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1 - sideInset, pos.Y + fireHeight + yOffset, pos.Z + 1, maxU, minV);
                // Backface
                ctx.Tess.addVertexWithUV(pos.X + 1 - sideInset, pos.Y + fireHeight + yOffset, pos.Z + 1, maxU, minV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1 - sideInset, pos.Y + fireHeight + yOffset, pos.Z, minU, minV);
            }

            // Climbing North Wall
            if (BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X, pos.Y, pos.Z - 1))
            {
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight + yOffset, pos.Z + sideInset, maxU, minV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight + yOffset, pos.Z + sideInset, minU, minV);
                // Backface
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight + yOffset, pos.Z + sideInset, minU, minV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight + yOffset, pos.Z + sideInset, maxU, minV);
            }

            // Climbing South Wall
            if (BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X, pos.Y, pos.Z + 1))
            {
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight + yOffset, pos.Z + 1 - sideInset, minU, minV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z + 1, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight + yOffset, pos.Z + 1 - sideInset, maxU, minV);
                // Backface
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight + yOffset, pos.Z + 1 - sideInset, maxU, minV);
                ctx.Tess.addVertexWithUV(pos.X, pos.Y + yOffset, pos.Z + 1, maxU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + yOffset, pos.Z + 1, minU, maxV);
                ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight + yOffset, pos.Z + 1 - sideInset, minU, minV);
            }

            // Climbing Ceilings
            if (BlockRegistry.Get("fire").IsFlammable(ctx.BlockReader, pos.X, pos.Y + 1, pos.Z))
            {
                float xMax = pos.X + 1, xMin = pos.X;
                float zMax = pos.Z + 1, zMin = pos.Z;

                ctx.Tess.setArrayLayer(firstFrame);
                minU = 0.0F;
                maxU = 1.0F;

                var ceilY = pos.Y + 1;
                var ceilOffset = -0.2F;

                if (((pos.X + ceilY + pos.Z) & 1) == 0)
                {
                    ctx.Tess.addVertexWithUV(xMin, ceilY + ceilOffset, pos.Z, maxU, minV);
                    ctx.Tess.addVertexWithUV(xMax, ceilY, pos.Z, maxU, maxV);
                    ctx.Tess.addVertexWithUV(xMax, ceilY, pos.Z + 1, minU, maxV);
                    ctx.Tess.addVertexWithUV(xMin, ceilY + ceilOffset, pos.Z + 1, minU, minV);

                    ctx.Tess.setArrayLayer(secondFrame);

                    ctx.Tess.addVertexWithUV(xMax, ceilY + ceilOffset, pos.Z + 1, maxU, minV);
                    ctx.Tess.addVertexWithUV(xMin, ceilY, pos.Z + 1, maxU, maxV);
                    ctx.Tess.addVertexWithUV(xMin, ceilY, pos.Z, minU, maxV);
                    ctx.Tess.addVertexWithUV(xMax, ceilY + ceilOffset, pos.Z, minU, minV);
                }
                else
                {
                    ctx.Tess.addVertexWithUV(pos.X, ceilY + ceilOffset, zMax, maxU, minV);
                    ctx.Tess.addVertexWithUV(pos.X, ceilY, zMin, maxU, maxV);
                    ctx.Tess.addVertexWithUV(pos.X + 1, ceilY, zMin, minU, maxV);
                    ctx.Tess.addVertexWithUV(pos.X + 1, ceilY + ceilOffset, zMax, minU, minV);

                    ctx.Tess.setArrayLayer(secondFrame);

                    ctx.Tess.addVertexWithUV(pos.X + 1, ceilY + ceilOffset, zMin, maxU, minV);
                    ctx.Tess.addVertexWithUV(pos.X + 1, ceilY, zMax, maxU, maxV);
                    ctx.Tess.addVertexWithUV(pos.X, ceilY, zMax, minU, maxV);
                    ctx.Tess.addVertexWithUV(pos.X, ceilY + ceilOffset, zMin, minU, minV);
                }
            }
        }
        else // Render central "X" flames for fire on solid floors
        {
            float insetSmall = 0.2f, insetLarge = 0.3f;
            float xC = pos.X + 0.5f, zC = pos.Z + 0.5f;

            // First diagonal set
            ctx.Tess.addVertexWithUV(xC - insetLarge, pos.Y + fireHeight, pos.Z + 1, maxU, minV);
            ctx.Tess.addVertexWithUV(xC + insetSmall, pos.Y, pos.Z + 1, maxU, maxV);
            ctx.Tess.addVertexWithUV(xC + insetSmall, pos.Y, pos.Z, minU, maxV);
            ctx.Tess.addVertexWithUV(xC - insetLarge, pos.Y + fireHeight, pos.Z, minU, minV);

            ctx.Tess.addVertexWithUV(xC + insetLarge, pos.Y + fireHeight, pos.Z, maxU, minV);
            ctx.Tess.addVertexWithUV(xC - insetSmall, pos.Y, pos.Z, maxU, maxV);
            ctx.Tess.addVertexWithUV(xC - insetSmall, pos.Y, pos.Z + 1, minU, maxV);
            ctx.Tess.addVertexWithUV(xC + insetLarge, pos.Y + fireHeight, pos.Z + 1, minU, minV);

            // Switch texture frame
            ctx.Tess.setArrayLayer(secondFrame);

            // Second diagonal set (X-axis dominant)
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight, zC + insetLarge, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, zC - insetSmall, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y, zC - insetSmall, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight, zC + insetLarge, minU, minV);

            ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight, zC - insetLarge, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y, zC + insetSmall, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, zC + insetSmall, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight, zC - insetLarge, minU, minV);

            // Third set (outer crossing)
            float i4 = 0.4f, i5 = 0.5f;
            ctx.Tess.addVertexWithUV(xC - i4, pos.Y + fireHeight, pos.Z, minU, minV);
            ctx.Tess.addVertexWithUV(xC - i5, pos.Y, pos.Z, minU, maxV);
            ctx.Tess.addVertexWithUV(xC - i5, pos.Y, pos.Z + 1, maxU, maxV);
            ctx.Tess.addVertexWithUV(xC - i4, pos.Y + fireHeight, pos.Z + 1, maxU, minV);

            ctx.Tess.addVertexWithUV(xC + i4, pos.Y + fireHeight, pos.Z + 1, minU, minV);
            ctx.Tess.addVertexWithUV(xC + i5, pos.Y, pos.Z + 1, minU, maxV);
            ctx.Tess.addVertexWithUV(xC + i5, pos.Y, pos.Z, maxU, maxV);
            ctx.Tess.addVertexWithUV(xC + i4, pos.Y + fireHeight, pos.Z, maxU, minV);

            // Final set
            ctx.Tess.setArrayLayer(firstFrame);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight, zC + i4, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y, zC + i5, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, zC + i5, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight, zC + i4, maxU, minV);

            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y + fireHeight, zC - i4, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, zC - i5, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y, zC - i5, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X, pos.Y + fireHeight, zC - i4, maxU, minV);
        }

        return true;
    }
}
