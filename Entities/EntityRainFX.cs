using betareborn.Blocks;
using betareborn.Blocks.Materials;
using betareborn.Client.Rendering.Core;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityRainFX : EntityFX
    {

        public EntityRainFX(World var1, double var2, double var4, double var6) : base(var1, var2, var4, var6, 0.0D, 0.0D, 0.0D)
        {
            motionX *= (double)0.3F;
            motionY = (double)((float)java.lang.Math.random() * 0.2F + 0.1F);
            motionZ *= (double)0.3F;
            particleRed = 1.0F;
            particleGreen = 1.0F;
            particleBlue = 1.0F;
            particleTextureIndex = 19 + rand.nextInt(4);
            setBoundingBoxSpacing(0.01F, 0.01F);
            particleGravity = 0.06F;
            particleMaxAge = (int)(8.0D / (java.lang.Math.random() * 0.8D + 0.2D));
        }

        public override void renderParticle(Tessellator var1, float var2, float var3, float var4, float var5, float var6, float var7)
        {
            base.renderParticle(var1, var2, var3, var4, var5, var6, var7);
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            motionY -= (double)particleGravity;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.98F;
            motionY *= (double)0.98F;
            motionZ *= (double)0.98F;
            if (particleMaxAge-- <= 0)
            {
                markDead();
            }

            if (onGround)
            {
                if (java.lang.Math.random() < 0.5D)
                {
                    markDead();
                }

                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
            }

            Material var1 = worldObj.getMaterial(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ));
            if (var1.isFluid() || var1.isSolid())
            {
                double var2 = (double)((float)(MathHelper.floor_double(posY) + 1) - BlockFluid.getFluidHeightFromMeta(worldObj.getBlockMeta(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ))));
                if (posY < var2)
                {
                    markDead();
                }
            }

        }
    }

}