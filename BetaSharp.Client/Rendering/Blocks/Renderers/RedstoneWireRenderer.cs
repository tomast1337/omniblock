using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class RedstoneWireRenderer : IBlockRenderer
{
    private const float QuarterPixel = 0.015625F;
    private const float ShroudVOffset = 1.0F / 16.0F;

    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        // IsPowerProviderOrWire is an instance method (reads the wire's own JSON-configured
        // conductor/repeater set) — this renderer is only ever invoked for the redstone_wire
        // block itself, so `block` here already IS that instance's owning Block.
        RedstoneWireBehavior wireBehavior = (RedstoneWireBehavior)block.Redstone!;

        int powerLevel = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        int textureId = block.GetTexture(Side.Up, powerLevel);
        if (ctx.OverrideTexture >= 0) textureId = ctx.OverrideTexture;

        // --- 1. Calculate the Glow Color & Emissive Lighting ---
        float powerPercent = powerLevel / 15.0F;

        // The wire glows with its own charge, which is a floor on the block channel rather than a
        // brightness multiplier: powered wire should stay visible in the dark without the sky
        // channel making it brighter by day.
        LightLevels wireLight = block.GetLightLevels(ctx.Lighting, pos.X, pos.Y + 1, pos.Z);
        ctx.Tess.setLight(wireLight.Sky, Math.Max(wireLight.Block, powerLevel * 0.4F));

        const float luminance = 1.0F;

        float r = powerPercent * 0.6F + 0.4F;
        if (powerLevel == 0) r = 0.3F;

        float g = powerPercent * powerPercent * 0.7F - 0.5F;
        float b = powerPercent * powerPercent * 0.6F - 0.7F;
        if (g < 0.0F) g = 0.0F;
        if (b < 0.0F) b = 0.0F;

        ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);

        // --- 2. UV Mapping ---
        int texU = (textureId & 15) << 4;
        int texV = textureId & 240;
        float minU = texU / 256.0F;
        float maxU = (texU + 15.99F) / 256.0F;
        float minV = texV / 256.0F;
        float maxV = (texV + 15.99F) / 256.0F;

        // --- 3. Connection Logic ---
        bool connectsWest = wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X - 1, pos.Y, pos.Z, 1) ||
                            (!ctx.BlockReader.ShouldSuffocate(pos.X - 1, pos.Y, pos.Z) &&
                             wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X - 1, pos.Y - 1, pos.Z, -1));
        bool connectsEast = wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X + 1, pos.Y, pos.Z, 3) ||
                            (!ctx.BlockReader.ShouldSuffocate(pos.X + 1, pos.Y, pos.Z) &&
                             wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X + 1, pos.Y - 1, pos.Z, -1));
        bool connectsNorth = wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y, pos.Z - 1, 2) ||
                             (!ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z - 1) &&
                              wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y - 1, pos.Z - 1, -1));
        bool connectsSouth = wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y, pos.Z + 1, 0) ||
                             (!ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z + 1) &&
                              wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y - 1, pos.Z + 1, -1));

        if (!ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y + 1, pos.Z))
        {
            if (ctx.BlockReader.ShouldSuffocate(pos.X - 1, pos.Y, pos.Z) &&
                wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X - 1, pos.Y + 1, pos.Z, -1))
                connectsWest = true;
            if (ctx.BlockReader.ShouldSuffocate(pos.X + 1, pos.Y, pos.Z) &&
                wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X + 1, pos.Y + 1, pos.Z, -1))
                connectsEast = true;
            if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z - 1) &&
                wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y + 1, pos.Z - 1, -1))
                connectsNorth = true;
            if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z + 1) &&
                wireBehavior.IsPowerProviderOrWire(ctx.BlockReader, pos.X, pos.Y + 1, pos.Z + 1, -1))
                connectsSouth = true;
        }

        // --- 4. Determine Shape ---
        float renderMinX = pos.X, renderMaxX = pos.X + 1;
        float renderMinZ = pos.Z, renderMaxZ = pos.Z + 1;
        int shapeType = 0; // 0 = Cross, 1 = East/West, 2 = North/South

        if ((connectsWest || connectsEast) && !connectsNorth && !connectsSouth) shapeType = 1;
        if ((connectsNorth || connectsSouth) && !connectsEast && !connectsWest) shapeType = 2;

        if (shapeType != 0) // Use the "Straight Line" texture variant
        {
            minU = (texU + 16) / 256.0F;
            maxU = (texU + 16 + 15.99F) / 256.0F;
        }

        if (shapeType == 0)
        {
            if (connectsWest || connectsEast || connectsNorth || connectsSouth)
            {
                if (!connectsWest)
                {
                    renderMinX += 0.3125F;
                    minU += 0.01953125F;
                }

                if (!connectsEast)
                {
                    renderMaxX -= 0.3125F;
                    maxU -= 0.01953125F;
                }

                if (!connectsNorth)
                {
                    renderMinZ += 0.3125F;
                    minV += 0.01953125F;
                }

                if (!connectsSouth)
                {
                    renderMaxZ -= 0.3125F;
                    maxV -= 0.01953125F;
                }
            }
        }

        // --- 5. Render Horizontal Ground Quad ---
        float shadowY = pos.Y + QuarterPixel;
        float wireY = shadowY + 0.001F;

        // Handle UV Rotation for North/South (Shape 2)
        float u1 = minU, u2 = maxU, u3 = maxU, u4 = minU;
        float v1 = minV, v2 = minV, v3 = maxV, v4 = maxV;

        if (shapeType == 2)
        {
            u1 = maxU;
            u2 = maxU;
            u3 = minU;
            u4 = minU;
            v1 = maxV;
            v2 = minV;
            v3 = minV;
            v4 = maxV;
        }

        // Main Wire
        ctx.Tess.addVertexWithUV(renderMaxX, wireY, renderMaxZ, u3, v3);
        ctx.Tess.addVertexWithUV(renderMaxX, wireY, renderMinZ, u2, v2);
        ctx.Tess.addVertexWithUV(renderMinX, wireY, renderMinZ, u1, v1);
        ctx.Tess.addVertexWithUV(renderMinX, wireY, renderMaxZ, u4, v4);

        // Shadow Shroud

        ctx.Tess.setColorOpaque_F(0.5f, 0.5f, 0.5f);
        ctx.Tess.addVertexWithUV(renderMaxX, shadowY, renderMaxZ, u3, v3 + ShroudVOffset);
        ctx.Tess.addVertexWithUV(renderMaxX, shadowY, renderMinZ, u2, v2 + ShroudVOffset);
        ctx.Tess.addVertexWithUV(renderMinX, shadowY, renderMinZ, u1, v1 + ShroudVOffset);
        ctx.Tess.addVertexWithUV(renderMinX, shadowY, renderMaxZ, u4, v4 + ShroudVOffset);

        // --- 6. Render Slopes ---
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y + 1, pos.Z)) return true;

        // Reset to the straight texture variant for slopes
        minU = (texU + 16) / 256.0F;
        maxU = (texU + 16 + 15.99F) / 256.0F;
        minV = texV / 256.0F;
        maxV = (texV + 15.99F) / 256.0F;

        float slopeHeight = pos.Y + 1.021875F;

        // West Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X - 1, pos.Y, pos.Z) &&
            ctx.BlockReader.GetBlockId(pos.X - 1, pos.Y + 1, pos.Z) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 1, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 1, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 0, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 0, maxU, maxV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 1, maxU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 1, minU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 0, minU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 0, maxU, maxV + ShroudVOffset);
        }

        // East Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X + 1, pos.Y, pos.Z) &&
            ctx.BlockReader.GetBlockId(pos.X + 1, pos.Y + 1, pos.Z) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 1, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 1, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 0, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 0, minU, minV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 1, minU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 1, maxU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 0, maxU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 0, minU, minV + ShroudVOffset);
        }

        // North Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z - 1) &&
            ctx.BlockReader.GetBlockId(pos.X, pos.Y + 1, pos.Z - 1) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + QuarterPixel, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + QuarterPixel, minU, minV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + QuarterPixel, minU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + QuarterPixel, maxU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + QuarterPixel, maxU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + QuarterPixel, minU, minV + ShroudVOffset);
        }

        // South Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z + 1) &&
            ctx.BlockReader.GetBlockId(pos.X, pos.Y + 1, pos.Z + 1) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + 1 - QuarterPixel, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + 1 - QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, maxV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + 1 - QuarterPixel, minU, minV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + 1 - QuarterPixel, minU, maxV + ShroudVOffset);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, maxV + ShroudVOffset);
        }

        return true;
    }
}
