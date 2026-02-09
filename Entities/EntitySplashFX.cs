using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySplashFX : EntityRainFX
    {

        public EntitySplashFX(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1, var2, var4, var6)
        {
            particleGravity = 0.04F;
            ++particleTextureIndex;
            if (var10 == 0.0D && (var8 != 0.0D || var12 != 0.0D))
            {
                velocityX = var8;
                velocityY = var10 + 0.1D;
                velocityZ = var12;
            }

        }
    }

}