using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Client.Guis;
using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Blocks.Entities;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Entities;
using BetaSharp.Client.Rendering.Items;
using BetaSharp.Client.Rendering.UI;
using BetaSharp.Entities;
using BetaSharp.Items;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using SixLabors.Fonts;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;
using TextRenderer = BetaSharp.Client.Rendering.TextRenderer;

namespace BetaSharp.Client.UI.Rendering;

public class UIRenderer
{
    public TextureManager TextureManager => _context.TextureManager;
    public TextRenderer TextRenderer => _context.TextRenderer;
    private readonly ItemRenderer _itemRenderer = new();

    private float _translateX = 0;
    private float _translateY = 0;
    private uint _currentTint = 0xFFFFFFFF;
    private readonly Stack<Vector2D<float>> _translationStack = new();

    private bool _scissorEnabled;
    private (int X, int Y, int W, int H) _scissorRect;
    private readonly Stack<(bool Enabled, int X, int Y, int W, int H)> _scissorStack = new();
    private GameOptions _gameOptions => _context.Options;
    private Func<Vector2D<int>> _getDisplaySize => _context.DisplaySize;
    private TextureHandle _terrainTexture => _context.TerrainTexture;
    private TextureHandle _itemsTexture => _context.ItemsTexture;
    private UIBatchRenderer _batch => _context.UiBatchRenderer;
    private readonly UIContext _context;

    public UIRenderer(UIContext context)
    {
        _context = context;
    }


    public void Begin()
    {
        GLManager.GL.Disable(GLEnum.Lighting);
        GLManager.GL.Disable(GLEnum.DepthTest);
        GLManager.GL.Disable(GLEnum.CullFace);
        GLManager.GL.Color4(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.GL.Enable(GLEnum.Blend);
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        GLManager.GL.PushMatrix();

        _translateX = 0;
        _translateY = 0;
        _currentTint = 0xFFFFFFFF;
        _translationStack.Clear();
        _scissorEnabled = false;
        _scissorStack.Clear();

        Vector2D<int> displaySize = _getDisplaySize();
        ScaledResolution res = new(_gameOptions, displaySize.X, displaySize.Y);
        Matrix4X4<float> proj = Matrix4X4.CreateOrthographicOffCenter(0f, res.ScaledWidth, res.ScaledHeight, 0f, -1f, 1f);
        _batch.Begin(proj);
    }

    public void End()
    {
        _batch.End();
        GLManager.GL.PopMatrix();
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
    }


    public void PushColor(Color color)
    {
        uint newTint = (uint)color;
        if (_currentTint != newTint)
        {
            _batch.Flush();
            _currentTint = newTint;
        }
        GLManager.GL.Color4(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
    }

    public void PopColor()
    {
        _batch.Flush();
        _currentTint = 0xFFFFFFFF;
        GLManager.GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    public void SetDepthMask(bool flag)
    {
        _batch.Flush();
        GLManager.GL.DepthMask(flag);
    }

    public void SetAlphaTest(bool flag)
    {
        _batch.Flush();
        if (flag) GLManager.GL.Enable(GLEnum.AlphaTest);
        else GLManager.GL.Disable(GLEnum.AlphaTest);
    }

    public void PushBlend(GLEnum s, GLEnum d)
    {
        _batch.Flush();
        GLManager.GL.BlendFunc(s, d);
    }

    public void PopBlend()
    {
        _batch.Flush();
        GLManager.GL.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
    }

    public void ClearDepth()
    {
        _batch.Flush();
        GLManager.GL.Clear((ClearBufferMask)GLEnum.DepthBufferBit);
    }

    public void PushTranslate(float x, float y)
    {
        _translationStack.Push(new(_translateX, _translateY));
        _translateX += x;
        _translateY += y;
    }

    public void PopTranslate()
    {
        if (_translationStack.Count > 0)
        {
            Vector2D<float> prev = _translationStack.Pop();
            _translateX = prev.X;
            _translateY = prev.Y;
        }
        else
        {
            _translateX = 0;
            _translateY = 0;
        }

        if (MathF.Abs(_translateX) < 0.0001f) _translateX = 0;
        if (MathF.Abs(_translateY) < 0.0001f) _translateY = 0;
    }

    public void EnableClipping(int x, int y, int width, int height)
    {
        _batch.Flush();

        Vector2D<int> displaySize = _getDisplaySize();
        ScaledResolution res = new(_gameOptions, displaySize.X, displaySize.Y);

        float left = x + _translateX;
        float top = y + _translateY;
        float right = left + width;
        float bottom = top + height;

        int framebufferWidth = Display.getFramebufferWidth();
        int framebufferHeight = Display.getFramebufferHeight();
        float scaleX = framebufferWidth / (float)res.ScaledWidth;
        float scaleY = framebufferHeight / (float)res.ScaledHeight;

        int physicalLeft = (int)MathF.Floor(left * scaleX);
        int physicalTop = (int)MathF.Floor(top * scaleY);
        int physicalRight = (int)MathF.Ceiling(right * scaleX);
        int physicalBottom = (int)MathF.Ceiling(bottom * scaleY);

        int clampedLeft = Math.Clamp(physicalLeft, 0, framebufferWidth);
        int clampedTop = Math.Clamp(physicalTop, 0, framebufferHeight);
        int clampedRight = Math.Clamp(physicalRight, 0, framebufferWidth);
        int clampedBottom = Math.Clamp(physicalBottom, 0, framebufferHeight);

        int physicalX = clampedLeft;
        int physicalY = framebufferHeight - clampedBottom;
        int physicalWidth = clampedRight - clampedLeft;
        int physicalHeight = clampedBottom - clampedTop;

        if (_scissorEnabled)
        {
            int parentRight = _scissorRect.X + _scissorRect.W;
            int parentTop = _scissorRect.Y + _scissorRect.H;
            physicalX = Math.Max(physicalX, _scissorRect.X);
            physicalY = Math.Max(physicalY, _scissorRect.Y);
            physicalWidth = Math.Max(0, Math.Min(physicalX + physicalWidth, parentRight) - physicalX);
            physicalHeight = Math.Max(0, Math.Min(physicalY + physicalHeight, parentTop) - physicalY);
        }

        _scissorStack.Push((_scissorEnabled, _scissorRect.X, _scissorRect.Y, _scissorRect.W, _scissorRect.H));
        _scissorEnabled = true;
        _scissorRect = (physicalX, physicalY, physicalWidth, physicalHeight);
        GLManager.GL.Enable(GLEnum.ScissorTest);
        GLManager.GL.Scissor(physicalX, physicalY, (uint)physicalWidth, (uint)physicalHeight);
    }

    public void DisableClipping()
    {
        _batch.Flush();
        if (_scissorStack.TryPop(out var prev))
        {
            _scissorEnabled = prev.Enabled;
            _scissorRect = (prev.X, prev.Y, prev.W, prev.H);
            if (prev.Enabled)
            {
                GLManager.GL.Enable(GLEnum.ScissorTest);
                GLManager.GL.Scissor(prev.X, prev.Y, (uint)Math.Max(0, prev.W), (uint)Math.Max(0, prev.H));
                return;
            }
        }
        else
        {
            _scissorEnabled = false;
        }
        GLManager.GL.Disable(GLEnum.ScissorTest);
    }

    public void DrawRect(float x, float y, float width, float height, Color color)
    {
        float x1 = MathF.Floor(x + _translateX);
        float y1 = MathF.Floor(y + _translateY);
        float x2 = MathF.Floor(x + _translateX + width);
        float y2 = MathF.Floor(y + _translateY + height);
        _batch.AddColoredQuad(x1, y1, x2 - x1, y2 - y1, (uint)color);
    }

    public void DrawGradientRect(float x, float y, float width, float height, Color topColor, Color bottomColor)
    {
        float x1 = MathF.Floor(x + _translateX);
        float y1 = MathF.Floor(y + _translateY);
        float x2 = MathF.Floor(x + _translateX + width);
        float y2 = MathF.Floor(y + _translateY + height);
        _batch.AddGradientQuad(x1, y1, x2 - x1, y2 - y1, (uint)topColor, (uint)bottomColor);
    }

    public void DrawText(string text, float x, float y, Color color, float scale = 1.0f, bool shadow = true)
    {
        float ix = MathF.Floor(x + _translateX);
        float iy = MathF.Floor(y + _translateY);
        if (shadow)
            TextRenderer.DrawStringWithShadow(text, ix, iy, color, batch: _batch, scale: scale);
        else
            TextRenderer.DrawString(text, ix, iy, color, batch: _batch, scale: scale);
    }

    public void DrawTextWrapped(string text, float x, float y, float maxWidth, Color color)
    {
        TextRenderer.DrawStringWrapped(text, (int)MathF.Floor(x + _translateX), (int)MathF.Floor(y + _translateY), (int)maxWidth, color, batch: _batch);
    }

    public void DrawCenteredText(string text, float x, float y, Color color, float rotation = 0, float scale = 1.0f, bool shadow = true)
    {
        float pivotX = MathF.Floor(x + _translateX);
        float pivotY = MathF.Floor(y + _translateY);

        if (rotation == 0)
        {
            if (shadow)
                TextRenderer.DrawStringWithShadow(text, pivotX, pivotY, color, HorizontalAlignment.Center, _batch, scale);
            else
                TextRenderer.DrawString(text, pivotX, pivotY, color, HorizontalAlignment.Center, _batch, scale);
            return;
        }

        float rad = rotation * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        if (shadow)
            TextRenderer.DrawStringWithShadow(text, 0f, 0f, color, HorizontalAlignment.Center, _batch, scale, cos, sin, pivotX, pivotY);
        else
            TextRenderer.DrawString(text, 0f, 0f, color, HorizontalAlignment.Center, _batch, scale, cos, sin, pivotX, pivotY);
    }

    public void DrawTexture(TextureHandle texture, float x, float y, float width, float height)
    {
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);
        _batch.SetTexture((uint)texture.Id);
        _batch.AddQuad(finalX, finalY, finalX + width, finalY + height, 0f, 0f, 1f, 1f, _currentTint);
    }

    public void DrawTexturedModalRect(TextureHandle texture, float x, float y, float u, float v, float width, float height)
    {
        DrawTexturedModalRect(texture, x, y, u, v, width, height, width, height, 0.0f);
    }

    public void DrawTexturedModalRect(TextureHandle texture, float x, float y, float u, float v, float width, float height, float uvWidth, float uvHeight)
    {
        DrawTexturedModalRect(texture, x, y, u, v, width, height, uvWidth, uvHeight, 0.0f);
    }

    public void DrawTexturedModalRect(TextureHandle texture, float x, float y, float u, float v, float width, float height, float uvWidth, float uvHeight, float z)
    {
        const float f = 0.00390625F; // 1/256
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);
        _batch.SetTexture((uint)texture.Id);
        _batch.AddQuad(finalX, finalY, finalX + width, finalY + height,
            u * f, v * f, (u + uvWidth) * f, (v + uvHeight) * f,
            _currentTint);
    }

    public void DrawRepeatingTexture(TextureHandle texture, float x, float y, float width, float height, float textureScale, float scrollOffsetY = 0f)
    {
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);

        float u0 = finalX / textureScale;
        float v0 = (finalY + scrollOffsetY) / textureScale;
        float u1 = (finalX + width) / textureScale;
        float v1 = (finalY + height + scrollOffsetY) / textureScale;

        _batch.SetTexture((uint)texture.Id);
        _batch.AddQuad(finalX, finalY, finalX + width, finalY + height, u0, v0, u1, v1, (uint)Color.FromRgb(0x404040));
    }

    public void DrawItemIntoGui(ItemRenderer itemRenderer, int itemId, int itemMeta, int textureId, float x, float y)
    {
        bool isBlock3D = itemId < 256 && BlockRenderer.IsSideLit(Block.Blocks[itemId].getRenderType());

        if (isBlock3D)
        {
            _batch.Flush();
            GLManager.GL.Enable(GLEnum.RescaleNormal);
            Lighting.turnOnGui();
            itemRenderer.drawItemIntoGui(TextRenderer, TextureManager, itemId, itemMeta, textureId, (int)(x + _translateX), (int)(y + _translateY));
            Lighting.turnOff();
            GLManager.GL.Disable(GLEnum.RescaleNormal);
            return;
        }

        if (textureId < 0) return;

        TextureHandle texHandle = itemId < 256 ? _terrainTexture : _itemsTexture;

        int colorMultiplier = Item.ITEMS[itemId]!.getColorMultiplier(itemMeta);
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);
        float u0 = (textureId % 16 * 16) / 256f;
        float v0 = (textureId / 16 * 16) / 256f;
        _batch.SetTexture((uint)texHandle.Id);
        _batch.AddQuad(finalX, finalY, finalX + 16f, finalY + 16f, u0, v0, u0 + 16f / 256f, v0 + 16f / 256f, (uint)Color.FromRgb((uint)colorMultiplier));
    }

    public void DrawItem(ItemStack? stack, float x, float y)
    {
        if (stack == null) return;

        bool isBlock = stack.ItemId < 256 && BlockRenderer.IsSideLit(Block.Blocks[stack.ItemId].getRenderType());

        if (isBlock)
        {
            _batch.Flush();
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate(0, 0, 32.0f);

            GLManager.GL.Disable(GLEnum.CullFace);
            GLManager.GL.Enable(GLEnum.RescaleNormal);
            GLManager.GL.Enable(GLEnum.DepthTest);

            Lighting.turnOnGui();
            _itemRenderer.renderItemIntoGUI(TextRenderer, TextureManager, stack, (int)(x + _translateX), (int)(y + _translateY));
            Lighting.turnOff();

            GLManager.GL.Disable(GLEnum.CullFace);
            GLManager.GL.Disable(GLEnum.DepthTest);
            GLManager.GL.Disable(GLEnum.RescaleNormal);
            GLManager.GL.PopMatrix();
        }
        else
        {
            int iconIndex = stack.getTextureId();
            if (iconIndex < 0) return;

            TextureHandle texHandle = stack.ItemId < 256 ? _terrainTexture : _itemsTexture;

            int colorMultiplier = Item.ITEMS[stack.ItemId]!.getColorMultiplier(stack.getDamage());
            uint rgba = (uint)Color.FromRgb((uint)colorMultiplier);

            float finalX = MathF.Floor(x + _translateX);
            float finalY = MathF.Floor(y + _translateY);
            float u0 = (iconIndex % 16 * 16) / 256f;
            float v0 = (iconIndex / 16 * 16) / 256f;
            _batch.SetTexture((uint)texHandle.Id);
            _batch.AddQuad(finalX, finalY, finalX + 16f, finalY + 16f, u0, v0, u0 + 16f / 256f, v0 + 16f / 256f, rgba);
        }
    }

    public void DrawItemOverlay(ItemStack? stack, float x, float y)
    {
        if (stack == null) return;

        int bx = (int)(x + _translateX);
        int by = (int)(y + _translateY);

        if (stack.Count > 1)
        {
            string stackText = stack.Count.ToString();
            int textX = bx + 17 - TextRenderer.GetStringWidth(stackText);
            TextRenderer.DrawStringWithShadow(stackText, textX, by + 9, Color.White, batch: _batch);
        }

        if (stack.isDamaged())
        {
            int barWidth = (int)Math.Round(13.0 - stack.getDamage2() * 13.0 / stack.getMaxDamage());
            int damageColor = (int)Math.Round(255.0 - stack.getDamage2() * 255.0 / stack.getMaxDamage());
            int barColor = (255 - damageColor) << 16 | damageColor << 8;
            int bgColor = (255 - damageColor) / 4 << 16 | 16128;

            _batch.AddColoredQuad(bx + 2, by + 13, 13, 2, (uint)Color.FromRgb(0));
            _batch.AddColoredQuad(bx + 2, by + 13, 12, 1, (uint)Color.FromRgb((uint)bgColor));
            _batch.AddColoredQuad(bx + 2, by + 13, barWidth, 1, (uint)Color.FromRgb((uint)barColor));
        }
    }

    public void DrawEntity(Entity entity, float x, float y, float scale, float mouseX, float mouseY)
    {
        _batch.Flush();

        GLManager.GL.Enable(GLEnum.RescaleNormal);
        GLManager.GL.Enable(GLEnum.ColorMaterial);
        GLManager.GL.Enable(GLEnum.DepthTest);
        GLManager.GL.PushMatrix();
        GLManager.GL.Translate(x + _translateX, y + _translateY, 50.0F);

        GLManager.GL.Scale(-scale, scale, scale);
        GLManager.GL.Rotate(180.0F, 0.0F, 0.0F, 1.0F);
        GLManager.GL.Disable(GLEnum.CullFace);

        float bodyYaw = entity is EntityLiving el ? el.BodyYaw : entity.Yaw;
        float headYaw = entity.Yaw;
        float headPitch = entity.Pitch;
        float lookX = x + _translateX - mouseX;
        float lookY = y + _translateY - 50 - mouseY;

        GLManager.GL.Rotate(135.0F, 0.0F, 1.0F, 0.0F);
        Lighting.turnOn();
        GLManager.GL.Rotate(-135.0F, 0.0F, 1.0F, 0.0F);
        GLManager.GL.Rotate(-(float)Math.Atan(lookY / 40.0F) * 20.0F, 1.0F, 0.0F, 0.0F);

        if (entity is EntityLiving el2)
        {
            el2.BodyYaw = (float)Math.Atan(lookX / 40.0F) * 20.0F;
        }
        entity.Yaw = (float)Math.Atan(lookX / 40.0F) * 40.0F;
        entity.Pitch = -(float)Math.Atan(lookY / 40.0F) * 20.0F;
        entity.MinBrightness = 1.0F;

        GLManager.GL.Translate(0.0F, entity.StandingEyeHeight, 0.0F);
        EntityRenderDispatcher.Instance.PlayerViewY = 180.0F;
        EntityRenderDispatcher.Instance.RenderEntityWithPosYaw(entity, 0.0D, 0.0D, 0.0D, 0.0F, 1.0F);

        entity.MinBrightness = 0.0F;
        if (entity is EntityLiving el3)
        {
            el3.BodyYaw = bodyYaw;
        }
        entity.Yaw = headYaw;
        entity.Pitch = headPitch;

        GLManager.GL.PopMatrix();
        Lighting.turnOff();
        GLManager.GL.Disable(GLEnum.CullFace);
        GLManager.GL.Disable(GLEnum.DepthTest);
        GLManager.GL.Disable(GLEnum.RescaleNormal);
        GLManager.GL.Disable(GLEnum.ColorMaterial);
    }

    public void DrawScrollingText(string text, float x, float y, int containerWidth, int containerHeight, Color color, long scrollStartMs, int rightPadding = 2)
    {
        int availableWidth = containerWidth - (int)x - rightPadding;
        int textWidth = TextRenderer.GetStringWidth(text);

        if (availableWidth > 0 && textWidth > availableWidth)
        {
            float scrollOffset = scrollStartMs > 0 ? ComputeTextScrollOffset(textWidth - availableWidth, scrollStartMs) : 0f;
            EnableClipping((int)x, 0, availableWidth, containerHeight);
            DrawText(text, x - scrollOffset, y, color);
            DisableClipping();
        }
        else
        {
            DrawText(text, x, y, color);
        }
    }

    public void DrawScrollingCenteredText(string text, int containerWidth, int containerHeight, float textY, Color color, int padding = 2)
    {
        int availableWidth = containerWidth - padding * 2;
        int textWidth = TextRenderer.GetStringWidth(text);

        if (availableWidth > 0 && textWidth > availableWidth)
        {
            float scrollOffset = ComputeTextScrollOffset(textWidth - availableWidth);
            EnableClipping(padding, 0, availableWidth, containerHeight);
            DrawText(text, padding - scrollOffset, textY, color);
            DisableClipping();
        }
        else
        {
            DrawCenteredText(text, containerWidth / 2f, textY, color);
        }
    }

    private static float ComputeTextScrollOffset(int overflow) =>
        ComputeTextScrollOffset(overflow, 0L);

    private static float ComputeTextScrollOffset(int overflow, long startMs)
    {
        const float scrollSpeed = 30f;
        const float pauseSeconds = 1.0f;
        float scrollDuration = overflow / scrollSpeed;
        float period = (pauseSeconds + scrollDuration) * 2f;

        long elapsedMs = startMs > 0 ? Environment.TickCount64 - startMs : Environment.TickCount64;
        long periodMs = Math.Max(1L, (long)(period * 1000));
        float t = (float)(elapsedMs % periodMs) / 1000f;

        static float Smoothstep(float x) => x * x * (3f - 2f * x);

        float offset;
        if (t < pauseSeconds)
        {
            offset = 0f;
        }
        else if (t < pauseSeconds + scrollDuration)
        {
            float p = (t - pauseSeconds) / scrollDuration;
            offset = Smoothstep(p) * overflow;
        }
        else if (t < pauseSeconds * 2f + scrollDuration)
        {
            offset = overflow;
        }
        else
        {
            float p = (t - pauseSeconds * 2f - scrollDuration) / scrollDuration;
            offset = (1f - Smoothstep(p)) * overflow;
        }

        return Math.Clamp(offset, 0f, overflow);
    }

    public void DrawSign(BlockEntitySign sign, float x, float y, float scale)
    {
        _batch.Flush();

        GLManager.GL.Enable(GLEnum.RescaleNormal);
        GLManager.GL.Enable(GLEnum.DepthTest);
        GLManager.GL.PushMatrix();
        GLManager.GL.Translate(x + _translateX, y + _translateY, 50.0F);

        GLManager.GL.Scale(-scale, -scale, -scale);
        GLManager.GL.Rotate(180.0F, 0.0F, 1.0F, 0.0F);

        Block signBlock = sign.getBlock();
        if (signBlock == Block.Sign)
        {
            float rotation = sign.PushedBlockData * 360 / 16.0F;
            GLManager.GL.Rotate(rotation, 0.0F, 1.0F, 0.0F);
            GLManager.GL.Translate(0.0F, -1.0625F, 0.0F);
        }
        else
        {
            int rotationIndex = sign.PushedBlockData;
            float angle = 0.0F;
            if (rotationIndex == 2) angle = 180.0F;
            if (rotationIndex == 4) angle = 90.0F;
            if (rotationIndex == 5) angle = -90.0F;

            GLManager.GL.Rotate(angle, 0.0F, 1.0F, 0.0F);
            GLManager.GL.Translate(0.0F, -1.0625F, 0.0F);
        }

        BlockEntityRenderer.Instance.RenderTileEntityAt(sign, -0.5D, -0.75D, -0.5D, 0.0F);
        GLManager.GL.PopMatrix();
        GLManager.GL.Disable(GLEnum.DepthTest);
        GLManager.GL.Disable(GLEnum.RescaleNormal);
    }
}
