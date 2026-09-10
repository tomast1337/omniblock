using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Items;

public class ItemRenderer : EntityRenderer
{
    private readonly IBlockRuntimeView _blocks;
    private readonly JavaRandom random = new();
    public bool useCustomDisplayColor = true;

    public ItemRenderer(IBlockRuntimeView blocks)
    {
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        ShadowRadius = 0.15F;
        ShadowStrength = 12.0F / 16.0F;
    }

    public void doRenderItem(Entity entityItem, double x, double y, double z, float yaw, float tickDelta)
    {
        random.SetSeed(187L);
        var dropped = entityItem.Behaviors.Find<DroppedItemBehavior>();
        if (dropped?.Stack(entityItem) is not { } stack) return;

        var bobPhase = dropped.BobPhase(entityItem);
        var itemAge = dropped.ItemAge(entityItem);
        RenderSystem.ModelView.Push();
        var bobOffset = MathHelper.Sin((itemAge + tickDelta) / 10.0F + bobPhase) * 0.1F + 0.1F;
        var spinAngle = ((itemAge + tickDelta) / 20.0F + bobPhase) * (180.0F / (float)Math.PI);
        byte renderCount = 1;
        if (stack.Count > 1)
        {
            renderCount = 2;
        }

        if (stack.Count > 5)
        {
            renderCount = 3;
        }

        if (stack.Count > 20)
        {
            renderCount = 4;
        }

        RenderSystem.ModelView.Translate((float)x, (float)y + bobOffset, (float)z);
        float minU;
        float maxU;
        float minV;
        if (stack.ItemId < 256 && BlockRenderer.IsSideLit(_blocks.GetByProtocolId(stack.ItemId).RenderType))
        {
            RenderSystem.ModelView.Rotate(spinAngle, 0.0F, 1.0F, 0.0F);
            loadTexture("/terrain.png");
            var blockScale = 0.25F;
            if (!_blocks.GetByProtocolId(stack.ItemId).IsFullCube() && stack.ItemId != _blocks.Get("slab").Id
                                                                    && _blocks.GetByProtocolId(stack.ItemId).RenderType != BlockRendererType.PistonBase)
            {
                blockScale = 0.5F;
            }

            RenderSystem.ModelView.Scale(blockScale, blockScale, blockScale);

            for (var copyIndex = 0; copyIndex < renderCount; ++copyIndex)
            {
                RenderSystem.ModelView.Push();
                if (copyIndex > 0)
                {
                    minU = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    maxU = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    minV = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    RenderSystem.ModelView.Translate(minU, maxU, minV);
                }

                BlockRenderer.RenderBlockOnInventory(_blocks, _blocks.GetByProtocolId(stack.ItemId), stack.GetDamage(), entityItem.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
                RenderSystem.ModelView.Pop();
            }
        }
        else
        {
            RenderSystem.ModelView.Scale(0.5F, 0.5F, 0.5F);
            var iconIndex = stack.GetTextureId();
            if (stack.ItemId < 256)
            {
                loadTexture("/terrain.png");
            }
            else
            {
                loadTexture("/gui/items.png");
            }

            var tessellator = Tessellator.instance;
            minU = (iconIndex % 16 * 16 + 0) / 256.0F;
            maxU = (iconIndex % 16 * 16 + 16) / 256.0F;
            minV = (iconIndex / 16 * 16 + 0) / 256.0F;
            var maxV = (iconIndex / 16 * 16 + 16) / 256.0F;
            var quadWidth = 1.0F;
            var xOffset = 0.5F;
            var yOffset = 0.25F;
            int colorMultiplier;
            float red;
            float green;
            float blue;
            if (useCustomDisplayColor)
            {
                colorMultiplier = stack.GetItem().GetColorMultiplier(stack.GetDamage());
                red = ((colorMultiplier >> 16) & 255) / 255.0F;
                green = ((colorMultiplier >> 8) & 255) / 255.0F;
                blue = (colorMultiplier & 255) / 255.0F;
                var brightness = entityItem.GetBrightnessAtEyes(tickDelta);
                RenderSystem.Color = new Vector4D<float>(red * brightness, green * brightness, blue * brightness, 1.0F);
            }

            for (colorMultiplier = 0; colorMultiplier < renderCount; ++colorMultiplier)
            {
                RenderSystem.ModelView.Push();
                if (colorMultiplier > 0)
                {
                    red = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    green = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    blue = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    RenderSystem.ModelView.Translate(red, green, blue);
                }

                RenderSystem.ModelView.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
                tessellator.startDrawingQuads();
                tessellator.setNormal(0.0F, 1.0F, 0.0F);
                tessellator.addVertexWithUV(0.0F - xOffset, 0.0F - yOffset, 0.0D, minU, maxV);
                tessellator.addVertexWithUV(quadWidth - xOffset, 0.0F - yOffset, 0.0D, maxU, maxV);
                tessellator.addVertexWithUV(quadWidth - xOffset, 1.0F - yOffset, 0.0D, maxU, minV);
                tessellator.addVertexWithUV(0.0F - xOffset, 1.0F - yOffset, 0.0D, minU, minV);
                tessellator.draw(ProgramSlot.Entities);
                RenderSystem.ModelView.Pop();
            }
        }

        RenderSystem.ModelView.Pop();
    }

    public void drawItemIntoGui(TextRenderer fontRenderer, TextureManager textureManager, Item item, int itemDamage, int iconIndex, int x, int y)
    {
        var itemId = item.Id;
        float blue;
        if (itemId < 256 && BlockRenderer.IsSideLit(_blocks.GetByProtocolId(itemId).RenderType))
        {
            textureManager.BindTexture(textureManager.GetTextureId("/terrain.png"));
            var block = _blocks.GetByProtocolId(itemId);
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate(x - 2, y + 3, -3.0F);
            RenderSystem.ModelView.Scale(10.0F, 10.0F, 10.0F);
            RenderSystem.ModelView.Translate(1.0F, 0.5F, 1.0F);
            RenderSystem.ModelView.Scale(1.0F, 1.0F, -1.0F);
            RenderSystem.ModelView.Rotate(210.0F, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            var itemColor = item.GetColorMultiplier(itemDamage);
            blue = ((itemColor >> 16) & 255) / 255.0F;
            var greenChannel = ((itemColor >> 8) & 255) / 255.0F;
            var blueChannel = (itemColor & 255) / 255.0F;
            if (useCustomDisplayColor)
            {
                RenderSystem.Color = new Vector4D<float>(blue, greenChannel, blueChannel, 1.0F);
            }

            RenderSystem.ModelView.Rotate(-90.0F, 0.0F, 1.0F, 0.0F);
            BlockRenderer.RenderBlockOnInventory(_blocks, block, itemDamage, 1.0F, Tessellator.instance);
            RenderSystem.ModelView.Pop();
        }
        else if (iconIndex >= 0)
        {
            RenderSystem.LightingEnabled = false;
            if (itemId < 256)
            {
                textureManager.BindTexture(textureManager.GetTextureId("/terrain.png"));
            }
            else
            {
                textureManager.BindTexture(textureManager.GetTextureId("/gui/items.png"));
            }

            var colorMultiplier = item.GetColorMultiplier(itemDamage);
            var red = ((colorMultiplier >> 16) & 255) / 255.0F;
            var green = ((colorMultiplier >> 8) & 255) / 255.0F;
            blue = (colorMultiplier & 255) / 255.0F;
            if (useCustomDisplayColor)
            {
                RenderSystem.Color = new Vector4D<float>(red, green, blue, 1.0F);
            }

            renderTexturedQuad(x, y, iconIndex % 16 * 16, iconIndex / 16 * 16, 16, 16);
        }
    }

    public void renderItemIntoGUI(TextRenderer fontRenderer, TextureManager textureManager, ItemStack stack, int x, int y)
    {
        if (stack != null)
        {
            drawItemIntoGui(fontRenderer, textureManager, stack.GetItem(), stack.GetDamage(), stack.GetTextureId(), x, y);
        }
    }

    public void renderTexturedQuad(int x, int y, int u, int v, int width, int height)
    {
        var z = 0.0F;
        var uScale = 1 / 256f;
        var vScale = 1 / 256f;
        var tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(x + 0, y + height, z, (u + 0) * uScale, (v + height) * vScale);
        tessellator.addVertexWithUV(x + width, y + height, z, (u + width) * uScale, (v + height) * vScale);
        tessellator.addVertexWithUV(x + width, y + 0, z, (u + width) * uScale, (v + 0) * vScale);
        tessellator.addVertexWithUV(x + 0, y + 0, z, (u + 0) * uScale, (v + 0) * vScale);
        tessellator.draw(ProgramSlot.Gui);
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => doRenderItem(target, x, y, z, yaw, tickDelta);
}
