using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Client.Rendering.Blocks.Renderers;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Entities;

public class BlockEntityRendererPiston : BlockEntitySpecialRenderer
{
    private readonly PistonBaseRenderer _pistonBaseRenderer = new();
    private readonly PistonExtensionRenderer _pistonExtensionRenderer = new();

    public override void renderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta)
    {
        if (blockEntity is not BlockEntityPiston piston)
        {
            throw new ArgumentException("BlockEntity is not a Piston");
        }

        Block? block = Block.Blocks[piston.PushedBlockId];
        if (block == null) return;
        if (piston.GetProgress(tickDelta) < 1.0F)
        {
            Tessellator tess = Tessellator.instance;
            bindTextureByName("/terrain.png");
            Lighting.turnOff();

            // Culling off because the moving block is drawn with every face, including the ones
            // facing away. RenderState.Entity is that plus the depth behaviour a solid block
            // wants, and unlike the enables it replaces it is put back at the end: this used to
            // leave blending on for whatever block entity drew next, so a sign behind a moving
            // piston came out blended and a sign anywhere else did not.
            GLManager.State.ApplyUntrusted(RenderState.Entity with { Blend = BlendMode.Alpha });

            GLManager.GL.ShadeModel(GLEnum.Smooth);

            tess.startDrawingQuads();
            tess.setTranslationD(
                x - piston.X + piston.GetRenderOffsetX(tickDelta),
                y - piston.Y + piston.GetRenderOffsetY(tickDelta),
                z - piston.Z + piston.GetRenderOffsetZ(tickDelta)
            );

            tess.setColorOpaque(1, 1, 1);

            var baseCtx = new BlockRenderContext(
                blockReader: piston.World.Reader,
                lighting: piston.World.Lighting,
                tess: tess,
                renderAllFaces: true,
                aoBlendMode: 1
            );

            BlockPos pos = new(piston.X, piston.Y, piston.Z);

            if (block == BlockRegistry.Get("piston_head") && piston.GetProgress(tickDelta) < 0.5F)
            {
                var ctx = baseCtx with { CustomFlag = true };
                _pistonExtensionRenderer.Draw(block, pos, ref ctx);
            }
            else if (piston.IsSource && !piston.IsExtending)
            {
                var headCtx = baseCtx with { OverrideTexture = ((PistonBaseBehavior)block.Physics!).GetTopTexture(), CustomFlag = piston.GetProgress(tickDelta) < 0.5F };

                _pistonExtensionRenderer.Draw(BlockRegistry.Get("piston_head"), pos, ref headCtx);

                tess.setTranslationD(x - piston.X, y - piston.Y, z - piston.Z);

                var basePartCtx = baseCtx with { CustomFlag = true };
                _pistonBaseRenderer.Draw(block, pos, ref basePartCtx);
            }
            else
            {
                baseCtx.DrawBlock(block, pos);
            }

            tess.setTranslationD(0.0D, 0.0D, 0.0D);
            tess.draw();
            GLManager.State.ApplyUntrusted(RenderState.Entity);
            Lighting.turnOn();
        }
    }
}
