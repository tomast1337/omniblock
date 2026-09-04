using OmniBlock.Entities;

namespace OmniBlock.Client.Rendering.Entities.Models;

public abstract class ModelBase
{
    public bool IsRiding = false;
    public float OnGround;

    public virtual void Render(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }

    public virtual void SetRotationAngles(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
    }

    public virtual void SetLivingAnimations(EntityLiving entity, float limbSwing, float limbSwingAmount, float partialTick)
    {
    }
}
