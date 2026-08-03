using BetaSharp.Blocks;
using BetaSharp.Client.Rendering.Blocks;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Items;

namespace BetaSharp.Client.Rendering.Entities;

public class UndeadEntityRenderer : LivingEntityRenderer
{

    protected ModelBiped ModelBipedMain;

    public UndeadEntityRenderer(ModelBiped main, float shadowRadius) : base(main, shadowRadius)
    {
        ModelBipedMain = main;
    }

    protected override void RenderMore(EntityLiving entity, float tickDelta)
    {
        ItemStack heldItem = entity.HeldItem;
        if (heldItem != null)
        {
            GLManager.ModelView.Push();
            ModelBipedMain.BipedRightArm.Transform(1.0F / 16.0F);
            GLManager.ModelView.Translate(-(1.0F / 16.0F), 7.0F / 16.0F, 1.0F / 16.0F);
            float itemScale;
            if (heldItem.ItemId < 256 && BlockRenderer.IsSideLit(Block.Blocks[heldItem.ItemId].RenderType))
            {
                itemScale = 0.5F;
                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, -(5.0F / 16.0F));
                itemScale *= 12.0F / 16.0F;
                GLManager.ModelView.Rotate(20.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Scale(itemScale, -itemScale, itemScale);
            }
            else if (Item.Items[heldItem.ItemId].IsHandheld())
            {
                itemScale = 10.0F / 16.0F;
                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, 0.0F);
                GLManager.ModelView.Scale(itemScale, -itemScale, itemScale);
                GLManager.ModelView.Rotate(-100.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
            }
            else
            {
                itemScale = 6.0F / 16.0F;
                GLManager.ModelView.Translate(0.25F, 3.0F / 16.0F, -(3.0F / 16.0F));
                GLManager.ModelView.Scale(itemScale, itemScale, itemScale);
                GLManager.ModelView.Rotate(60.0F, 0.0F, 0.0F, 1.0F);
                GLManager.ModelView.Rotate(-90.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(20.0F, 0.0F, 0.0F, 1.0F);
            }

            Dispatcher.HeldItemRenderer.renderItem(entity, heldItem);
            GLManager.ModelView.Pop();
        }

    }
}
