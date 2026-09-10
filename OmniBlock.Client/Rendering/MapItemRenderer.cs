using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Entities;
using OmniBlock.Worlds.Maps;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.Rendering;

public class MapItemRenderer
{
    private readonly GameOptions _options;
    private readonly TextRenderer _textRenderer;
    private readonly TextureHandle _textureId;
    private readonly int[] colors = new int[128 * 128];

    public MapItemRenderer(TextRenderer textRenderer, GameOptions options, TextureManager textureManager)
    {
        _options = options;
        _textRenderer = textRenderer;
        _textureId = textureManager.Load(new Image<Rgba32>(128, 128));

        for (var i = 0; i < 128 * 128; ++i)
        {
            colors[i] = 0;
        }
    }

    public void render(EntityPlayer player, TextureManager textureManager, MapState mapState)
    {
        for (var i = 0; i < 128 * 128; ++i)
        {
            var color = mapState.Colors[i];
            if (color / 4 == 0)
            {
                // render translucent checkerboard pattern for transparent pixels
                colors[i] = (((i + i / 128) & 1) * 8 + 16) << 24;
            }
            else
            {
                var colorValue = MapColor.ById(color / 4).ColorValue;
                var lowest2Bits = color & 3;
                byte brightness = lowest2Bits switch
                {
                    0 => 180,
                    1 => 220,
                    _ => 255
                };

                var r = ((colorValue >> 16) & 255) * brightness / 255;
                var g = ((colorValue >> 8) & 255) * brightness / 255;
                var b = (colorValue & 255) * brightness / 255;

                colors[i] = unchecked((int)(0xFF000000u | (r << 16) | (g << 8) | b));
            }
        }

        if (_textureId.Texture != null) textureManager.Bind(colors, 128, 128, _textureId.Texture);
        var tess = Tessellator.instance;
        _textureId.Bind();
        // This used to enable blending without saying which one, inheriting whichever factors the
        // last thing to draw happened to leave behind. Ordinary transparency is what it wants and
        // what it got, so it says so now.
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });

        // Paired with the alpha test off, not on: the unexplored parts of the sheet are the
        // translucent checkerboard built above, and the alpha test would throw them away before
        // blending ever saw them.
        RenderSystem.AlphaTestEnabled = false;
        tess.startDrawingQuads();
        tess.addVertexWithUV(0, 128, -0.01F, 0.0D, 1.0D);
        tess.addVertexWithUV(128, 128, -0.01F, 1.0D, 1.0D);
        tess.addVertexWithUV(128, 0, -0.01F, 1.0D, 0.0D);
        tess.addVertexWithUV(0, 0, -0.01F, 0.0D, 0.0D);
        tess.draw(ProgramSlot.Hand);
        RenderSystem.AlphaTestEnabled = true;
        RenderSystem.State.Apply(RenderState.Entity);
        textureManager.BindTexture(textureManager.GetTextureId("/misc/mapicons.png"));
        foreach (var icon in mapState.Icons)
        {
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate((sbyte)icon.X / 2.0F + 64.0F, (sbyte)icon.Z / 2.0F + 64.0F, -0.02F);
            RenderSystem.ModelView.Rotate((sbyte)icon.Rotation * 360 / 16.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Scale(4.0F, 4.0F, 3.0F);
            RenderSystem.ModelView.Translate(-(2.0F / 16.0F), 2.0F / 16.0F, 0.0F);
            var uMin = (icon.Type % 4 + 0) / 4.0F;
            var vMin = (icon.Type / 4 + 0) / 4.0F;
            var uMax = (icon.Type % 4 + 1) / 4.0F;
            var vMax = (icon.Type / 4 + 1) / 4.0F;
            tess.startDrawingQuads();
            tess.addVertexWithUV(-1, 1, 0, uMin, vMin);
            tess.addVertexWithUV(1, 1, 0, uMax, vMin);
            tess.addVertexWithUV(1, -1, 0, uMax, vMax);
            tess.addVertexWithUV(-1, -1, 0, uMin, vMax);
            tess.draw(ProgramSlot.Hand);
            RenderSystem.ModelView.Pop();
        }

        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate(0.0F, 0.0F, -0.04F);
        RenderSystem.ModelView.Scale(1.0F, 1.0F, 1.0F);
        _textRenderer.DrawString(mapState.Id, 0, 0, Color.White);
        RenderSystem.ModelView.Pop();
    }
}
