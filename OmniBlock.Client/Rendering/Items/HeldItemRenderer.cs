using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Items;

public class HeldItemRenderer
{
    // The two cells the fire animation writes into, drawn as a crossed pair.
    private static readonly string[] s_fireLayers = ["fire_layer_0", "fire_layer_1"];

    private readonly OmniBlock _game;
    private readonly BlockRenderer blockRenderer = new();
    private readonly MapItemRenderer mapRenderer;
    private float equippedProgress;

    private int field_20099_f = -1;
    private ItemStack itemToRender;
    private float prevEquippedProgress;

    public HeldItemRenderer(OmniBlock game)
    {
        _game = game;
        mapRenderer = new MapItemRenderer(game.TextRenderer, game.Options, game.TextureManager);
    }

    public void renderItem(EntityLiving entity, ItemStack item)
    {
        RenderSystem.ModelView.Push();
        if (item.ItemId < 256 && BlockRenderer.IsSideLit(_game.Content.Blocks.GetByProtocolId(item.ItemId).RenderType))
        {
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId("/terrain.png"));
            BlockRenderer.RenderBlockOnInventory(_game.Content.Blocks, _game.Content.Blocks.GetByProtocolId(item.ItemId), item.GetDamage(), entity.GetBrightnessAtEyes(1.0F), Tessellator.instance);
        }
        else
        {
            var texPath = item.ItemId < 256 ? "/terrain.png" : "/gui/items.png";
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId(texPath));
            var tileSize = _game.TextureManager.GetAtlasTileSize(texPath);

            var tessellator = Tessellator.instance;
            var iconIndex = entity.GetItemStackTextureId(item);
            var minU = (iconIndex % 16 * 16 + 0.0F) / 256.0F;
            var maxU = (iconIndex % 16 * 16 + 15.99F) / 256.0F;
            var minV = (iconIndex / 16 * 16 + 0.0F) / 256.0F;
            var maxV = (iconIndex / 16 * 16 + 15.99F) / 256.0F;
            var quadWidth = 1.0F;
            var xOffset = 0.0F;
            var yOffset = 0.3F;
            RenderSystem.ModelView.Translate(-xOffset, -yOffset, 0.0F);
            var itemScale = 1.5F;
            RenderSystem.ModelView.Scale(itemScale, itemScale, itemScale);
            RenderSystem.ModelView.Rotate(50.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(335.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Translate(-(15.0F / 16.0F), -(1.0F / 16.0F), 0.0F);
            var thickness = 1.0F / 16.0F;
            tessellator.startDrawingQuads();
            tessellator.setNormal(0.0F, 0.0F, 1.0F);
            tessellator.addVertexWithUV(0.0D, 0.0D, 0.0D, maxU, maxV);
            tessellator.addVertexWithUV(quadWidth, 0.0D, 0.0D, minU, maxV);
            tessellator.addVertexWithUV(quadWidth, 1.0D, 0.0D, minU, minV);
            tessellator.addVertexWithUV(0.0D, 1.0D, 0.0D, maxU, minV);
            tessellator.draw(ProgramSlot.Hand);
            tessellator.startDrawingQuads();
            tessellator.setNormal(0.0F, 0.0F, -1.0F);
            tessellator.addVertexWithUV(0.0D, 1.0D, 0.0F - thickness, maxU, minV);
            tessellator.addVertexWithUV(quadWidth, 1.0D, 0.0F - thickness, minU, minV);
            tessellator.addVertexWithUV(quadWidth, 0.0D, 0.0F - thickness, minU, maxV);
            tessellator.addVertexWithUV(0.0D, 0.0D, 0.0F - thickness, maxU, maxV);
            tessellator.draw(ProgramSlot.Hand);
            tessellator.startDrawingQuads();
            tessellator.setNormal(-1.0F, 0.0F, 0.0F);

            int sliceIndex;
            float sliceProgress;
            float sliceU;
            float sliceX;
            for (sliceIndex = 0; sliceIndex < tileSize; ++sliceIndex)
            {
                sliceProgress = sliceIndex / (float)tileSize;
                sliceU = maxU + (minU - maxU) * sliceProgress - 1.0f / (tileSize * 32.0f);
                sliceX = quadWidth * sliceProgress;
                tessellator.addVertexWithUV(sliceX, 0.0D, 0.0F - thickness, sliceU, maxV);
                tessellator.addVertexWithUV(sliceX, 0.0D, 0.0D, sliceU, maxV);
                tessellator.addVertexWithUV(sliceX, 1.0D, 0.0D, sliceU, minV);
                tessellator.addVertexWithUV(sliceX, 1.0D, 0.0F - thickness, sliceU, minV);
            }

            tessellator.draw(ProgramSlot.Hand);
            tessellator.startDrawingQuads();
            tessellator.setNormal(1.0F, 0.0F, 0.0F);

            for (sliceIndex = 0; sliceIndex < tileSize; ++sliceIndex)
            {
                sliceProgress = sliceIndex / (float)tileSize;
                sliceU = maxU + (minU - maxU) * sliceProgress - 1.0f / (tileSize * 32.0f);
                sliceX = quadWidth * sliceProgress + 1.0F / tileSize;
                tessellator.addVertexWithUV(sliceX, 1.0D, 0.0F - thickness, sliceU, minV);
                tessellator.addVertexWithUV(sliceX, 1.0D, 0.0D, sliceU, minV);
                tessellator.addVertexWithUV(sliceX, 0.0D, 0.0D, sliceU, maxV);
                tessellator.addVertexWithUV(sliceX, 0.0D, 0.0F - thickness, sliceU, maxV);
            }

            tessellator.draw(ProgramSlot.Hand);
            tessellator.startDrawingQuads();
            tessellator.setNormal(0.0F, 1.0F, 0.0F);

            for (sliceIndex = 0; sliceIndex < tileSize; ++sliceIndex)
            {
                sliceProgress = sliceIndex / (float)tileSize;
                sliceU = maxV + (minV - maxV) * sliceProgress - 1.0f / (tileSize * 32.0f);
                sliceX = quadWidth * sliceProgress + 1.0F / tileSize;
                tessellator.addVertexWithUV(0.0D, sliceX, 0.0D, maxU, sliceU);
                tessellator.addVertexWithUV(quadWidth, sliceX, 0.0D, minU, sliceU);
                tessellator.addVertexWithUV(quadWidth, sliceX, 0.0F - thickness, minU, sliceU);
                tessellator.addVertexWithUV(0.0D, sliceX, 0.0F - thickness, maxU, sliceU);
            }

            tessellator.draw(ProgramSlot.Hand);
            tessellator.startDrawingQuads();
            tessellator.setNormal(0.0F, -1.0F, 0.0F);

            for (sliceIndex = 0; sliceIndex < tileSize; ++sliceIndex)
            {
                sliceProgress = sliceIndex / (float)tileSize;
                sliceU = maxV + (minV - maxV) * sliceProgress - 1.0f / (tileSize * 32.0f);
                sliceX = quadWidth * sliceProgress;
                tessellator.addVertexWithUV(quadWidth, sliceX, 0.0D, minU, sliceU);
                tessellator.addVertexWithUV(0.0D, sliceX, 0.0D, maxU, sliceU);
                tessellator.addVertexWithUV(0.0D, sliceX, 0.0F - thickness, maxU, sliceU);
                tessellator.addVertexWithUV(quadWidth, sliceX, 0.0F - thickness, minU, sliceU);
            }

            tessellator.draw(ProgramSlot.Hand);
        }

        RenderSystem.ModelView.Pop();
    }

    public void renderItemInFirstPerson(float tickDelta)
    {
        var equipProgress = prevEquippedProgress + (equippedProgress - prevEquippedProgress) * tickDelta;
        var player = _game.Player;
        var pitch = player.PrevPitch + (player.Pitch - player.PrevPitch) * tickDelta;
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Rotate(pitch, 1.0F, 0.0F, 0.0F);
        RenderSystem.ModelView.Rotate(player.PrevYaw + (player.Yaw - player.PrevYaw) * tickDelta, 0.0F, 1.0F, 0.0F);
        Lighting.turnOn();
        RenderSystem.ModelView.Pop();
        var heldStack = itemToRender;
        var brightness = _game.World.GetLuminance(MathHelper.Floor(player.X), MathHelper.Floor(player.Y), MathHelper.Floor(player.Z));
        float red;
        float sineSwing;
        float sqrtSwing;
        if (itemToRender != null)
        {
            var itemColor = itemToRender.GetItem().GetColorMultiplier(itemToRender.GetDamage());
            red = ((itemColor >> 16) & 255) / 255.0F;
            sineSwing = ((itemColor >> 8) & 255) / 255.0F;
            sqrtSwing = (itemColor & 255) / 255.0F;
            RenderSystem.Color = new Vector4D<float>(brightness * red, brightness * sineSwing, brightness * sqrtSwing, 1.0F);
        }
        else
        {
            RenderSystem.Color = new Vector4D<float>(brightness, brightness, brightness, 1.0F);
        }

        float baseScale;
        if (itemToRender != null && itemToRender.ItemId == _game.Content.Items.Get("omniblock:map").Id)
        {
            RenderSystem.ModelView.Push();
            baseScale = 0.8F;
            var swingProgress = player.GetSwingProgress(tickDelta);
            sineSwing = MathHelper.Sin(swingProgress * (float)Math.PI);
            sqrtSwing = MathHelper.Sin(MathHelper.Sqrt(swingProgress) * (float)Math.PI);
            RenderSystem.ModelView.Translate(-sqrtSwing * 0.4F, MathHelper.Sin(MathHelper.Sqrt(swingProgress) * (float)Math.PI * 2.0F) * 0.2F, -sineSwing * 0.2F);
            swingProgress = 1.0F - pitch / 45.0F + 0.1F;
            if (swingProgress < 0.0F)
            {
                swingProgress = 0.0F;
            }

            if (swingProgress > 1.0F)
            {
                swingProgress = 1.0F;
            }

            swingProgress = -MathHelper.Cos(swingProgress * (float)Math.PI) * 0.5F + 0.5F;
            RenderSystem.ModelView.Translate(0.0F, 0.0F * baseScale - (1.0F - equipProgress) * 1.2F - swingProgress * 0.5F + 0.04F, -0.9F * baseScale);
            RenderSystem.ModelView.Rotate(90.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(swingProgress * -85.0F, 0.0F, 0.0F, 1.0F);
            bindSkinTexture();

            for (var i = 0; i < 2; i++)
            {
                var handSide = i * 2 - 1;
                RenderSystem.ModelView.Push();
                RenderSystem.ModelView.Translate(-0.0F, -0.6F, 1.1F * handSide);
                RenderSystem.ModelView.Rotate(-45 * handSide, 1.0F, 0.0F, 0.0F);
                RenderSystem.ModelView.Rotate(-90.0F, 0.0F, 0.0F, 1.0F);
                RenderSystem.ModelView.Rotate(59.0F, 0.0F, 0.0F, 1.0F);
                RenderSystem.ModelView.Rotate(-65 * handSide, 0.0F, 1.0F, 0.0F);
                var playerRendererBase = EntityRenderDispatcher.Instance.GetEntityRenderObject(_game.Player);
                var playerRenderer = (PlayerEntityRenderer)playerRendererBase;
                var armScale = 1.0F;
                RenderSystem.ModelView.Scale(armScale, armScale, armScale);
                playerRenderer.DrawFirstPersonHand();
                RenderSystem.ModelView.Pop();
            }

            sineSwing = player.GetSwingProgress(tickDelta);
            sqrtSwing = MathHelper.Sin(sineSwing * sineSwing * (float)Math.PI);
            var secondarySwing = MathHelper.Sin(MathHelper.Sqrt(sineSwing) * (float)Math.PI);
            RenderSystem.ModelView.Rotate(-sqrtSwing * 20.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-secondarySwing * 20.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Rotate(-secondarySwing * 80.0F, 1.0F, 0.0F, 0.0F);
            sineSwing = 0.38F;
            RenderSystem.ModelView.Scale(sineSwing, sineSwing, sineSwing);
            RenderSystem.ModelView.Rotate(90.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(180.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Translate(-1.0F, -1.0F, 0.0F);
            sqrtSwing = 1 / 64f;
            RenderSystem.ModelView.Scale(sqrtSwing, sqrtSwing, sqrtSwing);
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId("/misc/mapbg.png"));
            var tessellator = Tessellator.instance;
            RenderSystem.Normal = new Vector3D<float>(0.0F, 0.0F, -1.0F);
            tessellator.startDrawingQuads();
            byte mapBorder = 7;
            tessellator.addVertexWithUV(0 - mapBorder, 128 + mapBorder, 0.0D, 0.0D, 1.0D);
            tessellator.addVertexWithUV(128 + mapBorder, 128 + mapBorder, 0.0D, 1.0D, 1.0D);
            tessellator.addVertexWithUV(128 + mapBorder, 0 - mapBorder, 0.0D, 1.0D, 0.0D);
            tessellator.addVertexWithUV(0 - mapBorder, 0 - mapBorder, 0.0D, 0.0D, 0.0D);
            tessellator.draw(ProgramSlot.Hand);
            var mapState = MapBehavior.GetMapState(itemToRender.GetDamage(), _game.World);
            mapRenderer.render(_game.Player, _game.TextureManager, mapState);
            RenderSystem.ModelView.Pop();
        }
        else if (itemToRender != null)
        {
            RenderSystem.ModelView.Push();
            baseScale = 0.8F;
            red = player.GetSwingProgress(tickDelta);
            sineSwing = MathHelper.Sin(red * (float)Math.PI);
            sqrtSwing = MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI);
            RenderSystem.ModelView.Translate(-sqrtSwing * 0.4F, MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI * 2.0F) * 0.2F, -sineSwing * 0.2F);
            RenderSystem.ModelView.Translate(0.7F * baseScale, -0.65F * baseScale - (1.0F - equipProgress) * 0.6F, -0.9F * baseScale);
            RenderSystem.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            red = player.GetSwingProgress(tickDelta);
            sineSwing = MathHelper.Sin(red * red * (float)Math.PI);
            sqrtSwing = MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI);
            RenderSystem.ModelView.Rotate(-sineSwing * 20.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-sqrtSwing * 20.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Rotate(-sqrtSwing * 80.0F, 1.0F, 0.0F, 0.0F);
            red = 0.4F;
            RenderSystem.ModelView.Scale(red, red, red);
            if (itemToRender.GetItem().IsHandheldRod())
            {
                RenderSystem.ModelView.Rotate(180.0F, 0.0F, 1.0F, 0.0F);
            }

            renderItem(player, itemToRender);
            RenderSystem.ModelView.Pop();
        }
        else
        {
            RenderSystem.ModelView.Push();
            baseScale = 0.8F;
            red = player.GetSwingProgress(tickDelta);
            sineSwing = MathHelper.Sin(red * (float)Math.PI);
            sqrtSwing = MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI);
            RenderSystem.ModelView.Translate(-sqrtSwing * 0.3F, MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI * 2.0F) * 0.4F, -sineSwing * 0.4F);
            RenderSystem.ModelView.Translate(0.8F * baseScale, -(12.0F / 16.0F) * baseScale - (1.0F - equipProgress) * 0.6F, -0.9F * baseScale);
            RenderSystem.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            red = player.GetSwingProgress(tickDelta);
            sineSwing = MathHelper.Sin(red * red * (float)Math.PI);
            sqrtSwing = MathHelper.Sin(MathHelper.Sqrt(red) * (float)Math.PI);
            RenderSystem.ModelView.Rotate(sqrtSwing * 70.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-sineSwing * 20.0F, 0.0F, 0.0F, 1.0F);
            bindSkinTexture();
            RenderSystem.ModelView.Translate(-1.0F, 3.6F, 3.5F);
            RenderSystem.ModelView.Rotate(120.0F, 0.0F, 0.0F, 1.0F);
            RenderSystem.ModelView.Rotate(200.0F, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Rotate(-135.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Scale(1.0F, 1.0F, 1.0F);
            RenderSystem.ModelView.Translate(5.6F, 0.0F, 0.0F);
            var playerRendererBase2 = EntityRenderDispatcher.Instance.GetEntityRenderObject(_game.Player);
            var playerRenderer2 = (PlayerEntityRenderer)playerRendererBase2;
            sqrtSwing = 1.0F;
            RenderSystem.ModelView.Scale(sqrtSwing, sqrtSwing, sqrtSwing);
            playerRenderer2.DrawFirstPersonHand();
            RenderSystem.ModelView.Pop();
        }

        Lighting.turnOff();
    }

    public void renderOverlays(float tickDelta)
    {
        RenderSystem.AlphaTestEnabled = false;
        int blockX;
        if (_game.Player.IsOnFire)
        {
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId("/terrain.png"));
            renderFireInFirstPerson(tickDelta);
        }

        if (_game.Player.IsInsideWall())
        {
            blockX = MathHelper.Floor(_game.Player.X);
            var blockY = MathHelper.Floor(_game.Player.Y);
            var blockZ = MathHelper.Floor(_game.Player.Z);
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId("/terrain.png"));
            var blockId = _game.World.Reader.GetBlockId(blockX, blockY, blockZ);
            if (_game.World.Reader.ShouldSuffocate(blockX, blockY, blockZ))
            {
                renderInsideOfBlock(tickDelta, _game.Content.Blocks.GetByProtocolId(blockId).GetTexture(Side.North));
            }
            else
            {
                for (var sampleIndex = 0; sampleIndex < 8; ++sampleIndex)
                {
                    var offsetX = ((sampleIndex >> 0) % 2 - 0.5F) * _game.Player.Width * 0.9F;
                    var offsetY = ((sampleIndex >> 1) % 2 - 0.5F) * _game.Player.Height * 0.2F;
                    var offsetZ = ((sampleIndex >> 2) % 2 - 0.5F) * _game.Player.Width * 0.9F;
                    var sampleX = MathHelper.Floor(blockX + offsetX);
                    var sampleY = MathHelper.Floor(blockY + offsetY);
                    var sampleZ = MathHelper.Floor(blockZ + offsetZ);
                    if (_game.World.Reader.ShouldSuffocate(sampleX, sampleY, sampleZ))
                    {
                        blockId = _game.World.Reader.GetBlockId(sampleX, sampleY, sampleZ);
                    }
                }
            }

            if (_game.Content.Blocks.TryGetByProtocolId(blockId, out var block))
            {
                renderInsideOfBlock(tickDelta, block.GetTexture(Side.North));
            }
        }

        if (_game.Player.IsInFluid(Material.Water))
        {
            _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId("/misc/water.png"));
            renderWarpedTextureOverlay(tickDelta);
        }

        RenderSystem.AlphaTestEnabled = true;
    }

    private void renderInsideOfBlock(float tickDelta, int textureId)
    {
        var tessellator = Tessellator.instance;
        _game.Player.GetBrightnessAtEyes(tickDelta);
        var brightness = 0.1F;
        RenderSystem.Color = new Vector4D<float>(brightness, brightness, brightness, 0.5F);
        RenderSystem.ModelView.Push();
        var minX = -1.0F;
        var maxX = 1.0F;
        var minY = -1.0F;
        var maxY = 1.0F;
        var z = -0.5F;
        var uvInset = 1 / 128f;
        var minU = textureId % 16 / 256.0F - uvInset;
        var maxU = (textureId % 16 + 15.99F) / 256.0F + uvInset;
        var minV = textureId / 16 / 256.0F - uvInset;
        var maxV = (textureId / 16 + 15.99F) / 256.0F + uvInset;
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(minX, minY, z, maxU, maxV);
        tessellator.addVertexWithUV(maxX, minY, z, minU, maxV);
        tessellator.addVertexWithUV(maxX, maxY, z, minU, minV);
        tessellator.addVertexWithUV(minX, maxY, z, maxU, minV);
        tessellator.draw(ProgramSlot.Hand);
        RenderSystem.ModelView.Pop();
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
    }

    private void renderWarpedTextureOverlay(float tickDelta)
    {
        var tessellator = Tessellator.instance;
        var brightness = _game.Player.GetBrightnessAtEyes(tickDelta);
        RenderSystem.Color = new Vector4D<float>(brightness, brightness, brightness, 0.5F);

        // Blended, but still depth tested and written, which is what a screen-filling quad drawn
        // right after the hand has always been. The depth buffer was cleared before the hand pass,
        // so the only thing this can be occluded by is the hand itself.
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });
        RenderSystem.ModelView.Push();
        var uvScale = 4.0F;
        var minX = -1.0F;
        var maxX = 1.0F;
        var minY = -1.0F;
        var maxY = 1.0F;
        var z = -0.5F;
        var uOffset = -_game.Player.Yaw / 64.0F;
        var vOffset = _game.Player.Pitch / 64.0F;
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(minX, minY, z, uvScale + uOffset, uvScale + vOffset);
        tessellator.addVertexWithUV(maxX, minY, z, 0.0F + uOffset, uvScale + vOffset);
        tessellator.addVertexWithUV(maxX, maxY, z, 0.0F + uOffset, 0.0F + vOffset);
        tessellator.addVertexWithUV(minX, maxY, z, uvScale + uOffset, 0.0F + vOffset);
        tessellator.draw(ProgramSlot.Hand);
        RenderSystem.ModelView.Pop();
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        RenderSystem.State.Apply(RenderState.Entity);
    }

    private void renderFireInFirstPerson(float tickDelta)
    {
        var tessellator = Tessellator.instance;
        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 0.9F);
        RenderSystem.State.Apply(RenderState.Entity with
        {
            Blend = BlendMode.Alpha
        });
        var quadSize = 1.0F;

        for (var layerIndex = 0; layerIndex < s_fireLayers.Length; ++layerIndex)
        {
            RenderSystem.ModelView.Push();
            var fireTexture = Atlases.Terrain.IndexOf(s_fireLayers[layerIndex]);
            var textureU = (fireTexture & 15) << 4;
            var textureV = fireTexture & 240;
            var minU = textureU / 256.0F;
            var maxU = (textureU + 15.99F) / 256.0F;
            var minV = textureV / 256.0F;
            var maxV = (textureV + 15.99F) / 256.0F;
            var minX = (0.0F - quadSize) / 2.0F;
            var maxX = minX + quadSize;
            var minY = 0.0F - quadSize / 2.0F;
            var maxY = minY + quadSize;
            var z = -0.5F;
            RenderSystem.ModelView.Translate(-(layerIndex * 2 - 1) * 0.24F, -0.3F, 0.0F);
            RenderSystem.ModelView.Rotate((layerIndex * 2 - 1) * 10.0F, 0.0F, 1.0F, 0.0F);
            tessellator.startDrawingQuads();
            tessellator.addVertexWithUV(minX, minY, z, maxU, maxV);
            tessellator.addVertexWithUV(maxX, minY, z, minU, maxV);
            tessellator.addVertexWithUV(maxX, maxY, z, minU, minV);
            tessellator.addVertexWithUV(minX, maxY, z, maxU, minV);
            tessellator.draw(ProgramSlot.Hand);
            RenderSystem.ModelView.Pop();
        }

        RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
        RenderSystem.State.Apply(RenderState.Entity);
    }

    public void updateEquippedItem()
    {
        prevEquippedProgress = equippedProgress;
        var player = _game.Player;
        var heldStack = player.Inventory.ItemInHand;
        var sameItem = field_20099_f == player.Inventory.SelectedSlot && heldStack == itemToRender;
        if (itemToRender == null && heldStack == null)
        {
            sameItem = true;
        }

        if (heldStack != null && itemToRender != null && heldStack != itemToRender && heldStack.ItemId == itemToRender.ItemId && heldStack.GetDamage() == itemToRender.GetDamage())
        {
            itemToRender = heldStack;
            sameItem = true;
        }

        var maxStep = 0.4F;
        var targetProgress = sameItem ? 1.0F : 0.0F;
        var progressDelta = targetProgress - equippedProgress;
        if (progressDelta < -maxStep)
        {
            progressDelta = -maxStep;
        }

        if (progressDelta > maxStep)
        {
            progressDelta = maxStep;
        }

        equippedProgress += progressDelta;
        if (equippedProgress < 0.1F)
        {
            itemToRender = heldStack;
            field_20099_f = player.Inventory.SelectedSlot;
        }
    }

    public void ResetEquippedProgress() => equippedProgress = 0.0F;

    private void bindSkinTexture()
    {
        var skinHandle = EntityRenderDispatcher.Instance.SkinManager?.GetTextureHandle(_game.Player?.Name);
        if (skinHandle != null)
        {
            skinHandle.Bind();
            return;
        }

        _game.TextureManager.BindTexture(_game.TextureManager.GetTextureId(_game.Player.GetTexture()));
    }
}
