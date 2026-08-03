using BetaSharp.Blocks;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class DoorRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        Box bounds = ctx.OverrideBounds ?? block.BoundingBox;

        var flatCtx = ctx with { EnableAo = false };

        const float lightBottom = 0.5F;
        const float lightTop = 1.0F;
        const float lightZ = 0.8F; // East/West
        const float lightX = 0.6F; // North/South

        float blockLuminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y, pos.Z);
        bool isLightEmitter = Block.BlocksLightLuminance[block.Id] > 0;

        // Dummy colors since Door uses flat shading (ctx.Tess.setColorOpaque_F) instead of AO
        FaceColors dummyColors = new FaceColors();

        // If your Helper specifically requires Vec3D instead of BlockPos, use this:
        Vec3D vecPos = new Vec3D(pos.X, pos.Y, pos.Z);

        // --- Bottom Face (Y - 1) ---
        float faceLuminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y - 1, pos.Z);
        if (bounds.MinY > 0.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightBottom * faceLuminance, lightBottom * faceLuminance, lightBottom * faceLuminance);
        flatCtx.DrawBottomFace(block, vecPos, dummyColors, block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Down));

        // --- Top Face (Y + 1) ---
        faceLuminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y + 1, pos.Z);
        if (bounds.MaxY < 1.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightTop * faceLuminance, lightTop * faceLuminance, lightTop * faceLuminance);
        flatCtx.DrawTopFace(block, vecPos, dummyColors, block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Up));

        // --- East Face (Z - 1) ---
        faceLuminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y, pos.Z - 1);
        if (bounds.MinZ > 0.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightZ * faceLuminance, lightZ * faceLuminance, lightZ * faceLuminance);
        int textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.North);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId; // Make it positive for the UV math
        }
        flatCtx.DrawEastFace(block, vecPos, dummyColors, textureId);

        // --- West Face (Z + 1) ---
        faceLuminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y, pos.Z + 1);
        if (bounds.MaxZ < 1.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightZ * faceLuminance, lightZ * faceLuminance, lightZ * faceLuminance);
        textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.South);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId;
        }
        flatCtx.DrawWestFace(block, vecPos, dummyColors, textureId);

        // --- North Face (X - 1) ---
        faceLuminance = block.GetLuminance(ctx.Lighting, pos.X - 1, pos.Y, pos.Z);
        if (bounds.MinX > 0.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightX * faceLuminance, lightX * faceLuminance, lightX * faceLuminance);
        textureId = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.West);


        if (textureId < 0)
        {
            flatCtx.FlipTexture = true;
            textureId = -textureId;
        }
        flatCtx.DrawNorthFace(block, vecPos, dummyColors, textureId);

        // --- South Face (X + 1) ---
        faceLuminance = block.GetLuminance(ctx.Lighting, pos.X + 1, pos.Y, pos.Z);
        if (bounds.MaxX < 1.0D) faceLuminance = blockLuminance;
        if (isLightEmitter) faceLuminance = 1.0F;

        ctx.Tess.setColorOpaque_F(lightX * faceLuminance, lightX * faceLuminance, lightX * faceLuminance);
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
