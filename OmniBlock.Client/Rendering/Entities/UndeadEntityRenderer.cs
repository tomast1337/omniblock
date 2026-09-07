using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Entities;

public class UndeadEntityRenderer : LivingEntityRenderer
{
    protected ModelBiped ModelBipedMain;

    public UndeadEntityRenderer(ModelBiped main, float shadowRadius) : base(main, shadowRadius) => ModelBipedMain = main;

    protected override void RenderMore(EntityLiving entity, float tickDelta)
    {
        var heldItem = entity.HeldItem;
        if (heldItem != null)
        {
            GLManager.ModelView.Push();
            ModelBipedMain.BipedRightArm.Transform(1.0F / 16.0F);
            GLManager.ModelView.Translate(-(1.0F / 16.0F), 7.0F / 16.0F, 1.0F / 16.0F);
            float itemScale;
            if (heldItem.ItemId < 256 && BlockRenderer.IsSideLit(global::OmniBlock.Registries.ContentRuntime.Current.Blocks.GetByProtocolId(heldItem.ItemId).RenderType))
            {
                itemScale = 0.5F;
                GLManager.ModelView.Translate(0.0F, 3.0F / 16.0F, -(5.0F / 16.0F));
                itemScale *= 12.0F / 16.0F;
                GLManager.ModelView.Rotate(20.0F, 1.0F, 0.0F, 0.0F);
                GLManager.ModelView.Rotate(45.0F, 0.0F, 1.0F, 0.0F);
                GLManager.ModelView.Scale(itemScale, -itemScale, itemScale);
            }
            else if (heldItem.GetItem().IsHandheld())
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
