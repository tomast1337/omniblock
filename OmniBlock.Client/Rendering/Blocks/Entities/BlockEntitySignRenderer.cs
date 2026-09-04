using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using Silk.NET.Maths;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering.Blocks.Entities;

public class BlockEntitySignRenderer : BlockEntitySpecialRenderer
{
    private readonly ModelSign _modelSign = new();

    public void renderTileEntitySignAt(BlockEntitySign sign, double x, double y, double z, float tickDelta)
    {
        var signBlock = sign.GetBlock();
        GLManager.ModelView.Push();
        var modelScale = 2.0F / 3.0F;
        float rotationYaw;
        if (signBlock == BlockRegistry.Get("sign"))
        {
            GLManager.ModelView.Translate((float)x + 0.5F, (float)y + 12.0F / 16.0F * modelScale, (float)z + 0.5F);
            var rotationDegrees = sign.PushedBlockData * 360 / 16.0F;
            GLManager.ModelView.Rotate(-rotationDegrees, 0.0F, 1.0F, 0.0F);
            _modelSign.SignStick.Visible = true;
        }
        else
        {
            var wallFacing = sign.PushedBlockData;
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

            GLManager.ModelView.Translate((float)x + 0.5F, (float)y + 12.0F / 16.0F * modelScale, (float)z + 0.5F);
            GLManager.ModelView.Rotate(-rotationYaw, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Translate(0.0F, -(5.0F / 16.0F), -(7.0F / 16.0F));
            _modelSign.SignStick.Visible = false;
        }

        bindTextureByName("/item/sign.png");
        GLManager.ModelView.Push();
        GLManager.ModelView.Scale(modelScale, -modelScale, -modelScale);

        _modelSign.Render();

        GLManager.ModelView.Pop();
        var fontRenderer = getFontRenderer();
        rotationYaw = (float)(1.0D / 60.0D) * modelScale;
        GLManager.ModelView.Translate(0.0F, 0.5F * modelScale, 0.07F * modelScale);
        GLManager.ModelView.Scale(rotationYaw, -rotationYaw, rotationYaw);
        GLManager.Normal = new Vector3D<float>(0.0F, 0.0F, -1.0F * rotationYaw);

        // The text sits a hair in front of the board and still writes to the same depth values
        // once rounded, so it is depth tested — a block in front of the sign still hides it — but
        // not depth written.
        GLManager.State.Apply(RenderState.Entity with
        {
            DepthWrite = false
        });

        for (var lineIndex = 0; lineIndex < sign.Texts.Length; ++lineIndex)
        {
            var lineText = sign.Texts[lineIndex];
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
        GLManager.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.ModelView.Pop();
    }

    public override void renderTileEntityAt(BlockEntity blockEntity, double x, double y, double z, float tickDelta) => renderTileEntitySignAt((BlockEntitySign)blockEntity, x, y, z, tickDelta);
}
