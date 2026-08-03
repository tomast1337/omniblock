using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities;
using BetaSharp.Client.Rendering.Entities.Models;
using Color = BetaSharp.Client.UI.Colors.Color;

namespace BetaSharp.Client.Rendering.Blocks.Entities;

public class BlockEntitySignRenderer : BlockEntitySpecialRenderer
{

    private readonly ModelSign _modelSign = new();

    public void renderTileEntitySignAt(BlockEntitySign sign, double x, double y, double z, float tickDelta)
    {
        Block signBlock = sign.GetBlock();
        GLManager.GL.PushMatrix();
        float modelScale = 2.0F / 3.0F;
        float rotationYaw;
        if (signBlock == BlockRegistry.Get("sign"))
        {
            GLManager.GL.Translate((float)x + 0.5F, (float)y + 12.0F / 16.0F * modelScale, (float)z + 0.5F);
            float rotationDegrees = sign.PushedBlockData * 360 / 16.0F;
            GLManager.GL.Rotate(-rotationDegrees, 0.0F, 1.0F, 0.0F);
            _modelSign.SignStick.Visible = true;
        }
        else
        {
            int wallFacing = sign.PushedBlockData;
            rotationYaw = 0.0F;
            if (wallFacing == 2)
            {
                rotationYaw = 180.0F;
            }

            if (wallFacing == 4)
            {
                rotationYaw = 90.0F;
            }

            if (wallFacing == 5)
            {
                rotationYaw = -90.0F;
            }

            GLManager.GL.Translate((float)x + 0.5F, (float)y + 12.0F / 16.0F * modelScale, (float)z + 0.5F);
            GLManager.GL.Rotate(-rotationYaw, 0.0F, 1.0F, 0.0F);
            GLManager.GL.Translate(0.0F, -(5.0F / 16.0F), -(7.0F / 16.0F));
            _modelSign.SignStick.Visible = false;
        }

        bindTextureByName("/item/sign.png");
        GLManager.GL.PushMatrix();
        GLManager.GL.Scale(modelScale, -modelScale, -modelScale);

        _modelSign.Render();

        // Block entities are drawn inside the instancing pass (WorldRenderer opens it around them)
        // and ModelSign is a BbModelEntityModel, so the board is queued rather than drawn. The text
        // below is not — it goes through the tessellator, straight to the screen — and it sits a
        // hair in front of the board, so the board has to reach the depth buffer first.
        EntityInstanceBatchRenderer.Instance.Flush();

        GLManager.GL.PopMatrix();
        TextRenderer fontRenderer = getFontRenderer();
        rotationYaw = (float)(1.0D / 60.0D) * modelScale;
        GLManager.GL.Translate(0.0F, 0.5F * modelScale, 0.07F * modelScale);
        GLManager.GL.Scale(rotationYaw, -rotationYaw, rotationYaw);
        GLManager.GL.Normal3(0.0F, 0.0F, -1.0F * rotationYaw);

        // The text sits a hair in front of the board and still writes to the same depth values
        // once rounded, so it is depth tested — a block in front of the sign still hides it — but
        // not depth written.
        GLManager.State.Apply(RenderState.Entity with { DepthWrite = false });

        for (int lineIndex = 0; lineIndex < sign.Texts.Length; ++lineIndex)
        {
            string lineText = sign.Texts[lineIndex];
            if (lineIndex == sign.CurrentRow)
            {
                lineText = "> " + lineText + " <";
                fontRenderer.DrawString(lineText, -fontRenderer.GetStringWidth(lineText) / 2, lineIndex * 10 - sign.Texts.Length * 5, Color.Black);
            }
            else
            {
                fontRenderer.DrawString(lineText, -fontRenderer.GetStringWidth(lineText) / 2, lineIndex * 10 - sign.Texts.Length * 5, Color.Black);
            }
        }

        GLManager.State.Apply(RenderState.Entity);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.GL.PopMatrix();
    }

    public override void renderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta)
    {
        renderTileEntitySignAt((BlockEntitySign)blockEntity, x, y, z, tickDelta);
    }
}
