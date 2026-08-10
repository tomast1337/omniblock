using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class RedstoneWireRenderer : IBlockRenderer
{
    private const float QuarterPixel = 0.015625F;

    /// <summary>
    ///     The wire's brighter twin, drawn underneath at half colour. Beta reached it as the tile one
    ///     row below the unpowered dust, which is what these two are.
    /// </summary>
    private static readonly int s_crossShroudLayer = Atlases.Terrain.LayerOf("omniblock:redstone_dust_cross_on");
    private static readonly int s_lineShroudLayer = Atlases.Terrain.LayerOf("omniblock:redstone_dust_line_on");

    /// <summary>A run of wire with no branch, which is the cross tile's neighbour on the atlas.</summary>
    private static readonly int s_lineLayer = Atlases.Terrain.LayerOf("omniblock:redstone_dust_line_off");

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
        // Four tiles: a cross and a straight run, each with a brighter twin drawn underneath as the
        // shroud. An override -- the block-breaking overlay -- has only the one texture to give, so
        // every part of the wire falls back to it.
        bool overridden = ctx.OverrideTexture >= 0;
        int wireLayer = Atlases.Terrain.LayerOfGridIndex(textureId);
        int shroudLayer = overridden ? wireLayer : s_crossShroudLayer;

        float minU = 0.0F;
        float maxU = 1.0F;
        float minV = 0.0F;
        float maxV = 1.0F;

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
            wireLayer = overridden ? wireLayer : s_lineLayer;
            shroudLayer = overridden ? wireLayer : s_lineShroudLayer;
        }

        if (shapeType == 0)
        {
            if (connectsWest || connectsEast || connectsNorth || connectsSouth)
            {
                if (!connectsWest)
                {
                    renderMinX += 0.3125F;
                    minU += 5.0F / 16.0F;
                }

                if (!connectsEast)
                {
                    renderMaxX -= 0.3125F;
                    maxU -= 5.0F / 16.0F;
                }

                if (!connectsNorth)
                {
                    renderMinZ += 0.3125F;
                    minV += 5.0F / 16.0F;
                }

                if (!connectsSouth)
                {
                    renderMaxZ -= 0.3125F;
                    maxV -= 5.0F / 16.0F;
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
        ctx.Tess.setArrayLayer(wireLayer);
        ctx.Tess.addVertexWithUV(renderMaxX, wireY, renderMaxZ, u3, v3);
        ctx.Tess.addVertexWithUV(renderMaxX, wireY, renderMinZ, u2, v2);
        ctx.Tess.addVertexWithUV(renderMinX, wireY, renderMinZ, u1, v1);
        ctx.Tess.addVertexWithUV(renderMinX, wireY, renderMaxZ, u4, v4);

        // Shadow Shroud

        ctx.Tess.setColorOpaque_F(0.5f, 0.5f, 0.5f);
        ctx.Tess.setArrayLayer(shroudLayer);
        ctx.Tess.addVertexWithUV(renderMaxX, shadowY, renderMaxZ, u3, v3);
        ctx.Tess.addVertexWithUV(renderMaxX, shadowY, renderMinZ, u2, v2);
        ctx.Tess.addVertexWithUV(renderMinX, shadowY, renderMinZ, u1, v1);
        ctx.Tess.addVertexWithUV(renderMinX, shadowY, renderMaxZ, u4, v4);

        // --- 6. Render Slopes ---
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y + 1, pos.Z)) return true;

        // Reset to the straight texture variant for slopes
        wireLayer = overridden ? wireLayer : s_lineLayer;
        shroudLayer = overridden ? wireLayer : s_lineShroudLayer;

        minU = 0.0F;
        maxU = 1.0F;
        minV = 0.0F;
        maxV = 1.0F;

        float slopeHeight = pos.Y + 1.021875F;

        // West Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X - 1, pos.Y, pos.Z) &&
            ctx.BlockReader.GetBlockId(pos.X - 1, pos.Y + 1, pos.Z) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.setArrayLayer(wireLayer);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 1, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 1, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 0, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 0, maxU, maxV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.setArrayLayer(shroudLayer);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 1, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 1, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, pos.Y, pos.Z + 0, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + QuarterPixel, slopeHeight, pos.Z + 0, maxU, maxV);
        }

        // East Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X + 1, pos.Y, pos.Z) &&
            ctx.BlockReader.GetBlockId(pos.X + 1, pos.Y + 1, pos.Z) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.setArrayLayer(wireLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 1, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 1, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 0, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 0, minU, minV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.setArrayLayer(shroudLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 1, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 1, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, slopeHeight, pos.Z + 0, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1 - QuarterPixel, pos.Y, pos.Z + 0, minU, minV);
        }

        // North Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z - 1) &&
            ctx.BlockReader.GetBlockId(pos.X, pos.Y + 1, pos.Z - 1) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.setArrayLayer(wireLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + QuarterPixel, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + QuarterPixel, minU, minV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.setArrayLayer(shroudLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + QuarterPixel, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + QuarterPixel, minU, minV);
        }

        // South Slope
        if (ctx.BlockReader.ShouldSuffocate(pos.X, pos.Y, pos.Z + 1) &&
            ctx.BlockReader.GetBlockId(pos.X, pos.Y + 1, pos.Z + 1) == block.Id)
        {
            ctx.Tess.setColorOpaque_F(luminance * r, luminance * g, luminance * b);
            ctx.Tess.setArrayLayer(wireLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + 1 - QuarterPixel, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + 1 - QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, maxV);

            ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);
            ctx.Tess.setArrayLayer(shroudLayer);
            ctx.Tess.addVertexWithUV(pos.X + 1, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1, pos.Y, pos.Z + 1 - QuarterPixel, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0, pos.Y, pos.Z + 1 - QuarterPixel, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0, slopeHeight, pos.Z + 1 - QuarterPixel, maxU, maxV);
        }

        return true;
    }
}
