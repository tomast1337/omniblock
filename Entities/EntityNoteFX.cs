using betareborn.Client.Rendering.Core;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityNoteFX : EntityFX
    {
        float field_21065_a;


        public EntityNoteFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : this(var1, var2, var4, var6, var8, var10, var12, 2.0F)
        {
        }

        public EntityNoteFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12, float var14) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            motionX *= (double)0.01F;
            motionY *= (double)0.01F;
            motionZ *= (double)0.01F;
            motionY += 0.2D;
            particleRed = MathHelper.sin(((float)var8 + 0.0F) * (float)Math.PI * 2.0F) * 0.65F + 0.35F;
            particleGreen = MathHelper.sin(((float)var8 + 1.0F / 3.0F) * (float)Math.PI * 2.0F) * 0.65F + 0.35F;
            particleBlue = MathHelper.sin(((float)var8 + 2.0F / 3.0F) * (float)Math.PI * 2.0F) * 0.65F + 0.35F;
            particleScale *= 12.0F / 16.0F;
            particleScale *= var14;
            field_21065_a = particleScale;
            particleMaxAge = 6;
            noClip = false;
            particleTextureIndex = 64;
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            float var8 = ((float)particleAge + var2) / (float)particleMaxAge * 32.0F;
            if (var8 < 0.0F)
            {
                var8 = 0.0F;
            }

            if (var8 > 1.0F)
            {
                var8 = 1.0F;
            }

            particleScale = field_21065_a * var8;
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            if (particleAge++ >= particleMaxAge)
            {
                markDead();
            }

            moveEntity(motionX, motionY, motionZ);
            if (posY == prevPosY)
            {
                motionX *= 1.1D;
                motionZ *= 1.1D;
            }

            motionX *= (double)0.66F;
            motionY *= (double)0.66F;
            motionZ *= (double)0.66F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

        }
    }

}