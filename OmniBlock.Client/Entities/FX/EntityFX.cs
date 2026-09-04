using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Entities.FX;

public class EntityFX : Entity
{
    public static double interpPosX;
    public static double interpPosY;
    public static double interpPosZ;
    protected int particleAge;
    protected float particleBlue;
    protected float particleGravity;
    protected float particleGreen;
    protected int particleMaxAge;
    protected float particleRed;
    protected float particleScale;
    protected int particleTextureIndex;
    protected float particleTextureJitterX;
    protected float particleTextureJitterY;

    public EntityFX(IWorldContext world, double x, double y, double z, double velocityX, double velocityY, double velocityZ) : base(world, null)
    {
        SetBoundingBoxSpacing(0.2F, 0.2F);
        StandingEyeHeight = Height / 2.0F;
        SetPosition(x, y, z);
        particleRed = particleGreen = particleBlue = 1.0F;
        VelocityX = velocityX + (float)(System.Random.Shared.NextDouble() * 2.0D - 1.0D) * 0.4F;
        VelocityY = velocityY + (float)(System.Random.Shared.NextDouble() * 2.0D - 1.0D) * 0.4F;
        VelocityZ = velocityZ + (float)(System.Random.Shared.NextDouble() * 2.0D - 1.0D) * 0.4F;
        var velocityScale = (float)(System.Random.Shared.NextDouble() + System.Random.Shared.NextDouble() + 1.0D) * 0.15F;
        var speed = MathHelper.Sqrt(VelocityX * VelocityX + VelocityY * VelocityY + VelocityZ * VelocityZ);
        VelocityX = VelocityX / speed * velocityScale * 0.4F;
        VelocityY = VelocityY / speed * velocityScale * 0.4F + 0.1F;
        VelocityZ = VelocityZ / speed * velocityScale * 0.4F;
        particleTextureJitterX = Random.NextFloat() * 3.0F;
        particleTextureJitterY = Random.NextFloat() * 3.0F;
        particleScale = (Random.NextFloat() * 0.5F + 0.5F) * 2.0F;
        particleMaxAge = (int)(4.0F / (Random.NextFloat() * 0.9F + 0.1F));
        particleAge = 0;
    }

    public EntityFX scaleVelocity(float multiplier)
    {
        VelocityX *= multiplier;
        VelocityY = (VelocityY - 0.1F) * multiplier + 0.1F;
        VelocityZ *= multiplier;
        return this;
    }

    public EntityFX scaleSize(float scale)
    {
        SetBoundingBoxSpacing(0.2F * scale, 0.2F * scale);
        particleScale *= scale;
        return this;
    }

    protected override bool BypassesSteppingEffects() => false;

    public override void Tick()
    {
        PrevX = X;
        PrevY = Y;
        PrevZ = Z;
        if (particleAge++ >= particleMaxAge)
        {
            MarkDead();
        }

        VelocityY -= 0.04D * particleGravity;
        Move(VelocityX, VelocityY, VelocityZ);
        VelocityX *= 0.98F;
        VelocityY *= 0.98F;
        VelocityZ *= 0.98F;
        if (OnGround)
        {
            VelocityX *= 0.7F;
            VelocityZ *= 0.7F;
        }
    }

    public virtual void renderParticle(Tessellator t, float partialTick, float rotX, float rotY, float rotZ, float upX, float upZ)
    {
        var minU = particleTextureIndex % 16 / 16.0F;
        var maxU = minU + 0.999F / 16.0F;
        var minV = particleTextureIndex / 16 / 16.0F;
        var maxV = minV + 0.999F / 16.0F;
        var size = 0.1F * particleScale;
        var x = (float)(PrevX + (X - PrevX) * partialTick - interpPosX);
        var y = (float)(PrevY + (Y - PrevY) * partialTick - interpPosY);
        var z = (float)(PrevZ + (Z - PrevZ) * partialTick - interpPosZ);
        var brightness = GetBrightnessAtEyes(partialTick);
        t.setColorOpaque_F(particleRed * brightness, particleGreen * brightness, particleBlue * brightness);
        t.addVertexWithUV(x - rotX * size - upX * size, y - rotY * size, z - rotZ * size - upZ * size, maxU, maxV);
        t.addVertexWithUV(x - rotX * size + upX * size, y + rotY * size, z - rotZ * size + upZ * size, maxU, minV);
        t.addVertexWithUV(x + rotX * size + upX * size, y + rotY * size, z + rotZ * size + upZ * size, minU, minV);
        t.addVertexWithUV(x + rotX * size - upX * size, y - rotY * size, z + rotZ * size - upZ * size, minU, maxV);
    }

    public virtual int getFXLayer() => 0;

    protected override void WriteNbt(NBTTagCompound nbt)
    {
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
    }
}
