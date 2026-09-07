using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class DoorRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var bounds = ctx.OverrideBounds ?? block.BoundingBox;

        var flatCtx = ctx with
        {
            EnableAo = false
        };

        const float lightBottom = 0.5F;
        const float lightTop = 1.0F;
        const float lightZ = 0.8F; // East/West
        const float lightX = 0.6F; // North/South

        var isLightEmitter = ctx.Blocks.GetLightEmission(block.Id) > 0;

        // Dummy colors since Door uses flat shading (ctx.Tess.setColorOpaque_F) instead of AO
        var dummyColors = new FaceColors();

        // If your Helper specifically requires Vec3D instead of BlockPos, use this:
        var vecPos = new Vec3D(pos.X, pos.Y, pos.Z);

        // --- Bottom Face (Y - 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MinY > 0.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X, pos.Y - 1, pos.Z);
        }

        ctx.Tess.setColorOpaque_F(lightBottom, lightBottom, lightBottom);
        flatCtx.DrawBottomFace(block, vecPos, dummyColors, block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Down));

        // --- Top Face (Y + 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MaxY < 1.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X, pos.Y + 1, pos.Z);
        }

        ctx.Tess.setColorOpaque_F(lightTop, lightTop, lightTop);
        flatCtx.DrawTopFace(block, vecPos, dummyColors, block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Up));

        // --- East Face (Z - 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MinZ > 0.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z - 1);
        }

        ctx.Tess.setColorOpaque_F(lightZ, lightZ, lightZ);
        var textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.North);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId; // Make it positive for the UV math
        }

        flatCtx.DrawEastFace(block, vecPos, dummyColors, textureId);

        // --- West Face (Z + 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MaxZ < 1.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z + 1);
        }

        ctx.Tess.setColorOpaque_F(lightZ, lightZ, lightZ);
        textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.South);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId;
        }

        flatCtx.DrawWestFace(block, vecPos, dummyColors, textureId);

        // --- North Face (X - 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MinX > 0.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X - 1, pos.Y, pos.Z);
        }

        ctx.Tess.setColorOpaque_F(lightX, lightX, lightX);
        textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.West);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId;
        }

        flatCtx.DrawNorthFace(block, vecPos, dummyColors, textureId);

        // --- South Face (X + 1) ---
        if (isLightEmitter)
        {
            ctx.SetFullBright();
        }
        else if (bounds.MaxX < 1.0D)
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }
        else
        {
            ctx.SetLightAt(block, pos.X + 1, pos.Y, pos.Z);
        }

        ctx.Tess.setColorOpaque_F(lightX, lightX, lightX);
        textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.East);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId;
        }

        flatCtx.DrawSouthFace(block, vecPos, dummyColors, textureId);

        return true;
    }
}
