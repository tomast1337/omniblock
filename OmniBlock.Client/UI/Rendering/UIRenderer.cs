using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Blocks.Entities;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.Rendering.UI;
using OmniBlock.Entities;
using OmniBlock.Items;
using Silk.NET.Maths;
using SixLabors.Fonts;
using Color = OmniBlock.Client.UI.Colors.Color;
using TextRenderer = OmniBlock.Client.Rendering.TextRenderer;

namespace OmniBlock.Client.UI.Rendering;

public class UIRenderer
{
    /// <summary>
    ///     Solid geometry shown inside the interface: a block in a slot, a mob in a preview, a sign.
    /// </summary>
    /// <remarks>
    ///     Unlike the flat panels around it these need the depth buffer, both tested and written, so
    ///     a model occludes its own far side. Culling stays off for the same reason it does in the
    ///     entity tree.
    /// </remarks>
    private static readonly RenderState s_preview =
        RenderState.Interface with
        {
            DepthTest = true,
            DepthWrite = true
        };

    private readonly UIContext _context;
    private readonly ItemRenderer _itemRenderer = new();
    private readonly Stack<(bool Enabled, int X, int Y, int W, int H)> _scissorStack = new();
    private readonly Stack<Vector2D<float>> _translationStack = new();
    private uint _currentTint = 0xFFFFFFFF;

    private bool _scissorEnabled;
    private (int X, int Y, int W, int H) _scissorRect;

    private float _translateX;
    private float _translateY;

    public UIRenderer(UIContext context) => _context = context;
    public UIContext Context => _context;
    public TextureManager TextureManager => _context.TextureManager;
    public TextRenderer TextRenderer => _context.TextRenderer;
    private GameOptions _gameOptions => _context.Options;
    private Func<Vector2D<int>> _getDisplaySize => _context.DisplaySize;
    private TextureHandle _terrainTexture => _context.TerrainTexture;
    private TextureHandle _itemsTexture => _context.ItemsTexture;
    private UIBatchRenderer _batch => _context.UiBatchRenderer;


    public void Begin()
    {
        // Lighting is a shader uniform rather than pipeline state, so it stays a separate call.
        GLManager.LightingEnabled = false;
        GLManager.State.Apply(RenderState.Interface);
        GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
        GLManager.ModelView.Push();

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
        GLManager.ModelView.Pop();
        GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
    }


    public void PushColor(Color color)
    {
        uint newTint = (uint)color;
        if (_currentTint != newTint)
        {
            _batch.Flush();
            _currentTint = newTint;
        }

        GLManager.Color = new(color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f);
    }

    public void PopColor()
    {
        _batch.Flush();
        _currentTint = 0xFFFFFFFF;
        GLManager.Color = new(1.0f, 1.0f, 1.0f, 1.0f);
    }

    public void SetAlphaTest(bool flag)
    {
        _batch.Flush();
        if (flag)
        {
            GLManager.AlphaTestEnabled = true;
        }
        else
        {
            GLManager.AlphaTestEnabled = false;
        }
    }

    /// <summary>
    ///     Draws the next thing with a different blend mode, until <see cref="PopBlend" />.
    /// </summary>
    /// <remarks>
    ///     Takes a <see cref="BlendMode" /> rather than a pair of GL factors, so a control says what
    ///     it wants rather than how the current backend spells it.
    /// </remarks>
    public void PushBlend(BlendMode mode)
    {
        // Flushed before the mode changes, so whatever is already queued is drawn under the blend
        // it was queued for rather than under the incoming one.
        _batch.Flush();
        _batch.Blend = mode;
        GLManager.State.Apply(RenderState.Interface with
        {
            Blend = mode
        });
    }

    public void PopBlend()
    {
        _batch.Flush();
        _batch.Blend = BlendMode.Alpha;
        GLManager.State.Apply(RenderState.Interface);
    }

    public void ClearDepth()
    {
        _batch.Flush();

        // Depth writing has to be on for this to do anything: a depth clear is masked by the depth
        // write mask, and RenderState.Interface has it off. Clearing without it silently leaves the
        // buffer alone, which is how the item on the cursor ended up losing the depth test against
        // the block previews already drawn in the slots underneath it.
        GLManager.State.Apply(RenderState.Interface with
        {
            DepthWrite = true
        });

        // No depth clear here: a depth buffer is cleared by the pass that begins on it and there is
        // no command to clear one partway through. Doing this properly means ending the interface
        // pass here and beginning another, which the draw target has no way to ask for yet; until
        // then the item on the cursor can lose the depth test against a slot preview.
        GLManager.State.Apply(RenderState.Interface);
    }

    public void PushTranslate(float x, float y)
    {
        _translationStack.Push(new Vector2D<float>(_translateX, _translateY));
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

        if (MathF.Abs(_translateX) < 0.0001f)
        {
            _translateX = 0;
        }

        if (MathF.Abs(_translateY) < 0.0001f)
        {
            _translateY = 0;
        }
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

        // Scissor rectangles are relative to the framebuffer currently bound for drawing, which is
        // the offscreen FBO the world and screens render into (GameRenderer wraps CurrentScreen.Render
        // in FramebufferManager.Begin/End). That FBO is the size of the window normally, but is
        // resized to the ImGui viewport while the F3 overlay hosts the game — so measuring against
        // the window's framebuffer overstates the scale and pushes the clip rect right, cutting the
        // left edge off every row of the world and server lists. The origin stays at zero either
        // way: the FBO starts at the viewport, it does not contain it.
        Vector2D<int> renderTarget = _context.RenderTargetSize;
        int framebufferWidth = Math.Max(1, renderTarget.X);
        int framebufferHeight = Math.Max(1, renderTarget.Y);
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

        GLManager.Scissor = new ScissorRect(physicalX, physicalY, physicalWidth, physicalHeight);
    }

    public void DisableClipping()
    {
        _batch.Flush();
        if (_scissorStack.TryPop(out (bool Enabled, int X, int Y, int W, int H) prev))
        {
            _scissorEnabled = prev.Enabled;
            _scissorRect = (prev.X, prev.Y, prev.W, prev.H);
            if (prev.Enabled)
            {
                GLManager.Scissor = new ScissorRect(prev.X, prev.Y, Math.Max(0, prev.W), Math.Max(0, prev.H));
                return;
            }
        }
        else
        {
            _scissorEnabled = false;
        }

        GLManager.Scissor = null;
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
        {
            TextRenderer.DrawStringWithShadow(text, ix, iy, color, batch: _batch, scale: scale);
        }
        else
        {
            TextRenderer.DrawString(text, ix, iy, color, batch: _batch, scale: scale);
        }
    }

    public void DrawTextWrapped(string text, float x, float y, float maxWidth, Color color) => TextRenderer.DrawStringWrapped(text, (int)MathF.Floor(x + _translateX), (int)MathF.Floor(y + _translateY), (int)maxWidth, color, batch: _batch);

    public void DrawCenteredText(string text, float x, float y, Color color, float rotation = 0, float scale = 1.0f, bool shadow = true)
    {
        float pivotX = MathF.Floor(x + _translateX);
        float pivotY = MathF.Floor(y + _translateY);

        if (rotation == 0)
        {
            if (shadow)
            {
                TextRenderer.DrawStringWithShadow(text, pivotX, pivotY, color, HorizontalAlignment.Center, _batch, scale);
            }
            else
            {
                TextRenderer.DrawString(text, pivotX, pivotY, color, HorizontalAlignment.Center, _batch, scale);
            }

            return;
        }

        float rad = rotation * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        if (shadow)
        {
            TextRenderer.DrawStringWithShadow(text, 0f, 0f, color, HorizontalAlignment.Center, _batch, scale, cos, sin, pivotX, pivotY);
        }
        else
        {
            TextRenderer.DrawString(text, 0f, 0f, color, HorizontalAlignment.Center, _batch, scale, cos, sin, pivotX, pivotY);
        }
    }

    public void DrawTexture(TextureHandle texture, float x, float y, float width, float height)
    {
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);
        _batch.SetTexture((uint)texture.Id);
        _batch.AddQuad(finalX, finalY, finalX + width, finalY + height, 0f, 0f, 1f, 1f, _currentTint);
    }

    public void DrawTexturedModalRect(TextureHandle texture, float x, float y, float u, float v, float width, float height) => DrawTexturedModalRect(texture, x, y, u, v, width, height, width, height, 0.0f);

    public void DrawTexturedModalRect(TextureHandle texture, float x, float y, float u, float v, float width, float height, float uvWidth, float uvHeight) =>
        DrawTexturedModalRect(texture, x, y, u, v, width, height, uvWidth, uvHeight, 0.0f);

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
        bool isBlock3D = itemId < 256 && BlockRenderer.IsSideLit(BlockRegistry.GetByProtocolId(itemId).RenderType);

        if (isBlock3D)
        {
            _batch.Flush();
            Lighting.turnOnGui();
            itemRenderer.drawItemIntoGui(TextRenderer, TextureManager, _context.Content.Items.GetByProtocolId(itemId), itemMeta, textureId, (int)(x + _translateX), (int)(y + _translateY));
            Lighting.turnOff();
            return;
        }

        if (textureId < 0)
        {
            return;
        }

        TextureHandle texHandle = itemId < 256 ? _terrainTexture : _itemsTexture;

        int colorMultiplier = _context.Content.Items.GetByProtocolId(itemId).GetColorMultiplier(itemMeta);
        float finalX = MathF.Floor(x + _translateX);
        float finalY = MathF.Floor(y + _translateY);
        float u0 = textureId % 16 * 16 / 256f;
        float v0 = textureId / 16 * 16 / 256f;
        _batch.SetTexture((uint)texHandle.Id);
        _batch.AddQuad(finalX, finalY, finalX + 16f, finalY + 16f, u0, v0, u0 + 16f / 256f, v0 + 16f / 256f, (uint)Color.FromRgb((uint)colorMultiplier));
    }

    public void DrawItem(ItemStack? stack, float x, float y)
    {
        if (stack == null)
        {
            return;
        }

        bool isBlock = stack.ItemId < 256 && BlockRenderer.IsSideLit(BlockRegistry.GetByProtocolId(stack.ItemId).RenderType);

        if (isBlock)
        {
            _batch.Flush();
            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(0, 0, 32.0f);

            // Depth writing as well as testing: the block is solid geometry that has to occlude
            // its own far faces. RenderState.Interface does neither, which is right for flat panels
            // and wrong here.
            GLManager.State.Apply(s_preview);

            Lighting.turnOnGui();
            _itemRenderer.renderItemIntoGUI(TextRenderer, TextureManager, stack, (int)(x + _translateX), (int)(y + _translateY));
            Lighting.turnOff();

            GLManager.State.Apply(RenderState.Interface);
            GLManager.ModelView.Pop();
        }
        else
        {
            int iconIndex = stack.GetTextureId();
            if (iconIndex < 0)
            {
                return;
            }

            TextureHandle texHandle = stack.ItemId < 256 ? _terrainTexture : _itemsTexture;

            int colorMultiplier = stack.GetItem().GetColorMultiplier(stack.GetDamage());
            uint rgba = (uint)Color.FromRgb((uint)colorMultiplier);

            float finalX = MathF.Floor(x + _translateX);
            float finalY = MathF.Floor(y + _translateY);
            float u0 = iconIndex % 16 * 16 / 256f;
            float v0 = iconIndex / 16 * 16 / 256f;
            _batch.SetTexture((uint)texHandle.Id);
            _batch.AddQuad(finalX, finalY, finalX + 16f, finalY + 16f, u0, v0, u0 + 16f / 256f, v0 + 16f / 256f, rgba);
        }
    }

    public void DrawItemOverlay(ItemStack? stack, float x, float y)
    {
        if (stack == null)
        {
            return;
        }

        int bx = (int)(x + _translateX);
        int by = (int)(y + _translateY);

        if (stack.Count > 1)
        {
            string stackText = stack.Count.ToString();
            int textX = bx + 17 - TextRenderer.GetStringWidth(stackText);
            TextRenderer.DrawStringWithShadow(stackText, textX, by + 9, Color.White, batch: _batch);
        }

        if (stack.IsDamaged())
        {
            int barWidth = (int)Math.Round(13.0 - stack.GetDamage2() * 13.0 / stack.GetMaxDamage());
            int damageColor = (int)Math.Round(255.0 - stack.GetDamage2() * 255.0 / stack.GetMaxDamage());
            int barColor = ((255 - damageColor) << 16) | (damageColor << 8);
            int bgColor = (((255 - damageColor) / 4) << 16) | 16128;

            _batch.AddColoredQuad(bx + 2, by + 13, 13, 2, (uint)Color.FromRgb(0));
            _batch.AddColoredQuad(bx + 2, by + 13, 12, 1, (uint)Color.FromRgb((uint)bgColor));
            _batch.AddColoredQuad(bx + 2, by + 13, barWidth, 1, (uint)Color.FromRgb((uint)barColor));
        }
    }

    public void DrawEntity(Entity entity, float x, float y, float scale, float mouseX, float mouseY)
    {
        _batch.Flush();

        GLManager.State.Apply(s_preview);
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate(x + _translateX, y + _translateY, 50.0F);

        GLManager.ModelView.Scale(-scale, scale, scale);
        GLManager.ModelView.Rotate(180.0F, 0.0F, 0.0F, 1.0F);

        float bodyYaw = entity is EntityLiving el ? el.BodyYaw : entity.Yaw;
        float headYaw = entity.Yaw;
        float headPitch = entity.Pitch;
        float lookX = x + _translateX - mouseX;
        float lookY = y + _translateY - 50 - mouseY;

        GLManager.ModelView.Rotate(135.0F, 0.0F, 1.0F, 0.0F);
        Lighting.turnOn();
        GLManager.ModelView.Rotate(-135.0F, 0.0F, 1.0F, 0.0F);
        GLManager.ModelView.Rotate(-(float)Math.Atan(lookY / 40.0F) * 20.0F, 1.0F, 0.0F, 0.0F);

        if (entity is EntityLiving el2)
        {
            el2.BodyYaw = (float)Math.Atan(lookX / 40.0F) * 20.0F;
        }

        entity.Yaw = (float)Math.Atan(lookX / 40.0F) * 40.0F;
        entity.Pitch = -(float)Math.Atan(lookY / 40.0F) * 20.0F;
        entity.MinBrightness = 1.0F;

        GLManager.ModelView.Translate(0.0F, entity.StandingEyeHeight, 0.0F);
        EntityRenderDispatcher.Instance.PlayerViewY = 180.0F;
        EntityRenderDispatcher.Instance.RenderEntityWithPosYaw(entity, 0.0D, 0.0D, 0.0D, 0.0F, 1.0F);

        entity.MinBrightness = 0.0F;
        if (entity is EntityLiving el3)
        {
            el3.BodyYaw = bodyYaw;
        }

        entity.Yaw = headYaw;
        entity.Pitch = headPitch;

        GLManager.ModelView.Pop();
        Lighting.turnOff();
        GLManager.State.Apply(RenderState.Interface);
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
        float t = elapsedMs % periodMs / 1000f;

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

        GLManager.State.Apply(s_preview);
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate(x + _translateX, y + _translateY, 50.0F);

        GLManager.ModelView.Scale(-scale, -scale, -scale);
        GLManager.ModelView.Rotate(180.0F, 0.0F, 1.0F, 0.0F);

        Block signBlock = sign.GetBlock();
        if (signBlock == BlockRegistry.Get("sign"))
        {
            float rotation = sign.PushedBlockData * 360 / 16.0F;
            GLManager.ModelView.Rotate(rotation, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Translate(0.0F, -1.0625F, 0.0F);
        }
        else
        {
            int rotationIndex = sign.PushedBlockData;
            float angle = 0.0F;
            if (rotationIndex == 2)
            {
                angle = 180.0F;
            }

            if (rotationIndex == 4)
            {
                angle = 90.0F;
            }

            if (rotationIndex == 5)
            {
                angle = -90.0F;
            }

            GLManager.ModelView.Rotate(angle, 0.0F, 1.0F, 0.0F);
            GLManager.ModelView.Translate(0.0F, -1.0625F, 0.0F);
        }

        BlockEntityRenderer.Instance.RenderTileEntityAt(sign, -0.5D, -0.75D, -0.5D, 0.0F);
        GLManager.ModelView.Pop();
        GLManager.State.Apply(RenderState.Interface);
    }
}
