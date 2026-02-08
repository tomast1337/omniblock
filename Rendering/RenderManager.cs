using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Models;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Rendering
{
    public class RenderManager
    {
        private Dictionary<Class, Render> entityRenderMap = [];
        public static RenderManager instance = new RenderManager();
        private FontRenderer fontRenderer;
        public static double renderPosX;
        public static double renderPosY;
        public static double renderPosZ;
        public RenderEngine renderEngine;
        public ItemRenderer itemRenderer;
        public World worldObj;
        public EntityLiving livingPlayer;
        public float playerViewY;
        public float playerViewX;
        public GameSettings options;
        public double field_1222_l;
        public double field_1221_m;
        public double field_1220_n;

        private RenderManager()
        {
            registerRenderer(EntitySpider.Class, new RenderSpider());
            registerRenderer(EntityPig.Class, new RenderPig(new ModelPig(), new ModelPig(0.5F), 0.7F));
            registerRenderer(EntitySheep.Class, new RenderSheep(new ModelSheep2(), new ModelSheep1(), 0.7F));
            registerRenderer(EntityCow.Class, new RenderCow(new ModelCow(), 0.7F));
            registerRenderer(EntityWolf.Class, new RenderWolf(new ModelWolf(), 0.5F));
            registerRenderer(EntityChicken.Class, new RenderChicken(new ModelChicken(), 0.3F));
            registerRenderer(EntityCreeper.Class, new RenderCreeper());
            registerRenderer(EntitySkeleton.Class, new RenderBiped(new ModelSkeleton(), 0.5F));
            registerRenderer(EntityZombie.Class, new RenderBiped(new ModelZombie(), 0.5F));
            registerRenderer(EntitySlime.Class, new RenderSlime(new ModelSlime(16), new ModelSlime(0), 0.25F));
            registerRenderer(EntityPlayer.Class, new RenderPlayer());
            registerRenderer(EntityGiantZombie.Class, new RenderGiantZombie(new ModelZombie(), 0.5F, 6.0F));
            registerRenderer(EntityGhast.Class, new RenderGhast());
            registerRenderer(EntitySquid.Class, new RenderSquid(new ModelSquid(), 0.7F));
            registerRenderer(EntityLiving.Class, new RenderLiving(new ModelBiped(), 0.5F));
            registerRenderer(Entity.Class, new RenderEntity());
            registerRenderer(EntityPainting.Class, new RenderPainting());
            registerRenderer(EntityArrow.Class, new RenderArrow());
            registerRenderer(EntitySnowball.Class, new RenderSnowball(Item.snowball.getIconFromDamage(0)));
            registerRenderer(EntityEgg.Class, new RenderSnowball(Item.egg.getIconFromDamage(0)));
            registerRenderer(EntityFireball.Class, new RenderFireball());
            registerRenderer(EntityItem.Class, new RenderItem());
            registerRenderer(EntityTNTPrimed.Class, new RenderTNTPrimed());
            registerRenderer(EntityFallingSand.Class, new RenderFallingSand());
            registerRenderer(EntityMinecart.Class, new RenderMinecart());
            registerRenderer(EntityBoat.Class, new RenderBoat());
            registerRenderer(EntityFish.Class, new RenderFish());
            registerRenderer(EntityLightningBolt.Class, new RenderLightningBolt());

            foreach (var render in entityRenderMap.Values)
            {
                render.setRenderManager(this);
            }
        }

        private void registerRenderer(Class clazz, Render render)
        {
            entityRenderMap[clazz] = render;
        }

        public Render getEntityClassRenderObject(Class var1)
        {
            entityRenderMap.TryGetValue(var1, out Render? var2);
            if (var2 == null && var1 != Entity.Class)
            {
                var2 = getEntityClassRenderObject(var1.getSuperclass());
                registerRenderer(var1, var2);
            }

            return var2;
        }

        public Render getEntityRenderObject(Entity var1)
        {
            return getEntityClassRenderObject(var1.getClass());
        }

        public void cacheActiveRenderInfo(World var1, RenderEngine var2, FontRenderer var3, EntityLiving var4, GameSettings var5, float var6)
        {
            worldObj = var1;
            renderEngine = var2;
            options = var5;
            livingPlayer = var4;
            fontRenderer = var3;
            if (var4.isPlayerSleeping())
            {
                int var7 = var1.getBlockId(MathHelper.floor_double(var4.posX), MathHelper.floor_double(var4.posY), MathHelper.floor_double(var4.posZ));
                if (var7 == Block.BED.id)
                {
                    int var8 = var1.getBlockMeta(MathHelper.floor_double(var4.posX), MathHelper.floor_double(var4.posY), MathHelper.floor_double(var4.posZ));
                    int var9 = var8 & 3;
                    playerViewY = (float)(var9 * 90 + 180);
                    playerViewX = 0.0F;
                }
            }
            else
            {
                playerViewY = var4.prevRotationYaw + (var4.rotationYaw - var4.prevRotationYaw) * var6;
                playerViewX = var4.prevRotationPitch + (var4.rotationPitch - var4.prevRotationPitch) * var6;
            }

            field_1222_l = var4.lastTickPosX + (var4.posX - var4.lastTickPosX) * (double)var6;
            field_1221_m = var4.lastTickPosY + (var4.posY - var4.lastTickPosY) * (double)var6;
            field_1220_n = var4.lastTickPosZ + (var4.posZ - var4.lastTickPosZ) * (double)var6;
        }

        public void renderEntity(Entity var1, float var2)
        {
            double var3 = var1.lastTickPosX + (var1.posX - var1.lastTickPosX) * (double)var2;
            double var5 = var1.lastTickPosY + (var1.posY - var1.lastTickPosY) * (double)var2;
            double var7 = var1.lastTickPosZ + (var1.posZ - var1.lastTickPosZ) * (double)var2;
            float var9 = var1.prevRotationYaw + (var1.rotationYaw - var1.prevRotationYaw) * var2;
            float var10 = var1.getEntityBrightness(var2);
            GLManager.GL.Color3(var10, var10, var10);
            renderEntityWithPosYaw(var1, var3 - renderPosX, var5 - renderPosY, var7 - renderPosZ, var9, var2);
        }

        public void renderEntityWithPosYaw(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            Render var10 = getEntityRenderObject(var1);
            if (var10 != null)
            {
                var10.doRender(var1, var2, var4, var6, var8, var9);
                var10.doRenderShadowAndFire(var1, var2, var4, var6, var8, var9);
            }

        }

        public void func_852_a(World var1)
        {
            worldObj = var1;
        }

        public double func_851_a(double var1, double var3, double var5)
        {
            double var7 = var1 - field_1222_l;
            double var9 = var3 - field_1221_m;
            double var11 = var5 - field_1220_n;
            return var7 * var7 + var9 * var9 + var11 * var11;
        }

        public FontRenderer getFontRenderer()
        {
            return fontRenderer;
        }
    }

}