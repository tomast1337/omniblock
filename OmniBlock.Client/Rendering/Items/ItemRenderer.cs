using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Items;

public class ItemRenderer : EntityRenderer
{
    private readonly JavaRandom random = new();
    public bool useCustomDisplayColor = true;

    public ItemRenderer()
    {
        ShadowRadius = 0.15F;
        ShadowStrength = 12.0F / 16.0F;
    }

    public void doRenderItem(Entity entityItem, double x, double y, double z, float yaw, float tickDelta)
    {
        random.SetSeed(187L);
        DroppedItemBehavior? dropped = entityItem.Behaviors.Find<DroppedItemBehavior>();
        if (dropped?.Stack(entityItem) is not { } stack) return;

        float bobPhase = dropped.BobPhase(entityItem);
        int itemAge = dropped.ItemAge(entityItem);
        GLManager.ModelView.Push();
        float bobOffset = MathHelper.Sin((itemAge + tickDelta) / 10.0F + bobPhase) * 0.1F + 0.1F;
        float spinAngle = ((itemAge + tickDelta) / 20.0F + bobPhase) * (180.0F / (float)Math.PI);
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

        GLManager.ModelView.Translate((float)x, (float)y + bobOffset, (float)z);
        float minU;
        float maxU;
        float minV;
        if (stack.ItemId < 256 && BlockRenderer.IsSideLit(BlockRegistry.GetByProtocolId(stack.ItemId).RenderType))
        {
            GLManager.ModelView.Rotate(spinAngle, 0.0F, 1.0F, 0.0F);
            loadTexture("/terrain.png");
            float blockScale = 0.25F;
            if (!BlockRegistry.GetByProtocolId(stack.ItemId).IsFullCube() && stack.ItemId != BlockRegistry.Get("slab").Id
                && BlockRegistry.GetByProtocolId(stack.ItemId).RenderType != BlockRendererType.PistonBase)
            {
                blockScale = 0.5F;
            }

            GLManager.ModelView.Scale(blockScale, blockScale, blockScale);

            for (int copyIndex = 0; copyIndex < renderCount; ++copyIndex)
            {
                GLManager.ModelView.Push();
                if (copyIndex > 0)
                {
                    minU = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    maxU = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    minV = (random.NextFloat() * 2.0F - 1.0F) * 0.2F / blockScale;
                    GLManager.ModelView.Translate(minU, maxU, minV);
                }

                BlockRenderer.RenderBlockOnInventory(BlockRegistry.GetByProtocolId(stack.ItemId), stack.GetDamage(), entityItem.GetBrightnessAtEyes(tickDelta), Tessellator.instance);
                GLManager.ModelView.Pop();
            }
        }
        else
        {
            GLManager.ModelView.Scale(0.5F, 0.5F, 0.5F);
            int iconIndex = stack.GetTextureId();
            if (stack.ItemId < 256)
            {
                loadTexture("/terrain.png");
            }
            else
            {
                loadTexture("/gui/items.png");
            }

            Tessellator tessellator = Tessellator.instance;
            minU = (iconIndex % 16 * 16 + 0) / 256.0F;
            maxU = (iconIndex % 16 * 16 + 16) / 256.0F;
            minV = (iconIndex / 16 * 16 + 0) / 256.0F;
            float maxV = (iconIndex / 16 * 16 + 16) / 256.0F;
            float quadWidth = 1.0F;
            float xOffset = 0.5F;
            float yOffset = 0.25F;
            int colorMultiplier;
            float red;
            float green;
            float blue;
            if (useCustomDisplayColor)
            {
                colorMultiplier = Item.Items[stack.ItemId].GetColorMultiplier(stack.GetDamage());
                red = (colorMultiplier >> 16 & 255) / 255.0F;
                green = (colorMultiplier >> 8 & 255) / 255.0F;
                blue = (colorMultiplier & 255) / 255.0F;
                float brightness = entityItem.GetBrightnessAtEyes(tickDelta);
                GLManager.Color = new(red * brightness, green * brightness, blue * brightness, 1.0F);
            }

            for (colorMultiplier = 0; colorMultiplier < renderCount; ++colorMultiplier)
            {
                GLManager.ModelView.Push();
                if (colorMultiplier > 0)
                {
                    red = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    green = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    blue = (random.NextFloat() * 2.0F - 1.0F) * 0.3F;
                    GLManager.ModelView.Translate(red, green, blue);
                }

                GLManager.ModelView.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
                tessellator.startDrawingQuads();
                tessellator.setNormal(0.0F, 1.0F, 0.0F);
                tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(0.0F - yOffset), 0.0D, (double)minU, (double)maxV);
                tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(0.0F - yOffset), 0.0D, (double)maxU, (double)maxV);
                tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(1.0F - yOffset), 0.0D, (double)maxU, (double)minV);
                tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(1.0F - yOffset), 0.0D, (double)minU, (double)minV);
                tessellator.draw(ProgramSlot.Entities);
                GLManager.ModelView.Pop();
            }
        }

        GLManager.ModelView.Pop();
    }

    public void drawItemIntoGui(TextRenderer fontRenderer, TextureManager textureManager, int itemId, int itemDamage, int iconIndex, int x, int y)
    {
        float blue;
        if (itemId < 256 && BlockRenderer.IsSideLit(BlockRegistry.GetByProtocolId(itemId).RenderType))
        {
            textureManager.BindTexture(textureManager.GetTextureId("/terrain.png"));
            Block block = BlockRegistry.GetByProtocolId(itemId);
            GLManager.ModelView.Push();
            GLManager.ModelView.Translate(x - 2, y + 3, -3.0F);
            GLManager.ModelView.Scale(10.0F, 10.0F, 10.0F);
            GLManager.ModelView.Translate(1.0F, 0.5F, 1.0F);
            GLManager.ModelView.Scale(1.0F, 1.0F, -1.0F);
            GLManager.ModelView.Rotate(210.0F, 1.0F, 0.0F, 0.0F);
            GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            int itemColor = Item.Items[itemId].GetColorMultiplier(itemDamage);
            blue = (itemColor >> 16 & 255) / 255.0F;
            float greenChannel = (itemColor >> 8 & 255) / 255.0F;
            float blueChannel = (itemColor & 255) / 255.0F;
            if (useCustomDisplayColor)
            {
                GLManager.Color = new(blue, greenChannel, blueChannel, 1.0F);
            }

            GLManager.ModelView.Rotate(-90.0F, 0.0F, 1.0F, 0.0F);
            BlockRenderer.RenderBlockOnInventory(block, itemDamage, 1.0F, Tessellator.instance);
            GLManager.ModelView.Pop();
        }
        else if (iconIndex >= 0)
        {
            GLManager.LightingEnabled = false;
            if (itemId < 256)
            {
                textureManager.BindTexture(textureManager.GetTextureId("/terrain.png"));
            }
            else
            {
                textureManager.BindTexture(textureManager.GetTextureId("/gui/items.png"));
            }

            int colorMultiplier = Item.Items[itemId].GetColorMultiplier(itemDamage);
            float red = (colorMultiplier >> 16 & 255) / 255.0F;
            float green = (colorMultiplier >> 8 & 255) / 255.0F;
            blue = (colorMultiplier & 255) / 255.0F;
            if (useCustomDisplayColor)
            {
                GLManager.Color = new(red, green, blue, 1.0F);
            }

            renderTexturedQuad(x, y, iconIndex % 16 * 16, iconIndex / 16 * 16, 16, 16);
        }
    }

    public void renderItemIntoGUI(TextRenderer fontRenderer, TextureManager textureManager, ItemStack stack, int x, int y)
    {
        if (stack != null)
        {
            drawItemIntoGui(fontRenderer, textureManager, stack.ItemId, stack.GetDamage(), stack.GetTextureId(), x, y);
        }
    }

    public void renderTexturedQuad(int x, int y, int u, int v, int width, int height)
    {
        float z = 0.0F;
        float uScale = 1 / 256f;
        float vScale = 1 / 256f;
        Tessellator tessellator = Tessellator.instance;
        tessellator.startDrawingQuads();
        tessellator.addVertexWithUV(x + 0, y + height, (double)z, (double)((u + 0) * uScale), (double)((v + height) * vScale));
        tessellator.addVertexWithUV(x + width, y + height, (double)z, (double)((u + width) * uScale), (double)((v + height) * vScale));
        tessellator.addVertexWithUV(x + width, y + 0, (double)z, (double)((u + width) * uScale), (double)((v + 0) * vScale));
        tessellator.addVertexWithUV(x + 0, y + 0, (double)z, (double)((u + 0) * uScale), (double)((v + 0) * vScale));
        tessellator.draw(ProgramSlot.Gui);
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        doRenderItem(target, x, y, z, yaw, tickDelta);
    }
}
