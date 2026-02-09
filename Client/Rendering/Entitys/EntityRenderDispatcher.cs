using betareborn.Blocks;
using betareborn.Client.Rendering.Core;
using betareborn.Client.Rendering.Entitys.Models;
using betareborn.Client.Rendering.Items;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Client.Rendering.Entitys
{
    public class EntityRenderDispatcher
    {
        private Dictionary<Class, EntityRenderer> entityRenderMap = [];
        public static EntityRenderDispatcher instance = new EntityRenderDispatcher();
        private TextRenderer fontRenderer;
        public static double offsetX;
        public static double offsetY;
        public static double offsetZ;
        public TextureManager textureManager;
        public HeldItemRenderer heldItemRenderer;
        public World world;
        public EntityLiving cameraEntity;
        public float playerViewY;
        public float playerViewX;
        public GameOptions options;
        public double x;
        public double y;
        public double z;

        private EntityRenderDispatcher()
        {
            registerRenderer(EntitySpider.Class, new SpiderEntityRenderer());
            registerRenderer(EntityPig.Class, new PigEntityRenderer(new ModelPig(), new ModelPig(0.5F), 0.7F));
            registerRenderer(EntitySheep.Class, new SheepEntityRenderer(new ModelSheep2(), new ModelSheep1(), 0.7F));
            registerRenderer(EntityCow.Class, new CowEntityRenderer(new ModelCow(), 0.7F));
            registerRenderer(EntityWolf.Class, new WolfEntityRenderer(new ModelWolf(), 0.5F));
            registerRenderer(EntityChicken.Class, new ChickenEntityRenderer(new ModelChicken(), 0.3F));
            registerRenderer(EntityCreeper.Class, new CreeperEntityRenderer());
            registerRenderer(EntitySkeleton.Class, new UndeadEntityRenderer(new ModelSkeleton(), 0.5F));
            registerRenderer(EntityZombie.Class, new UndeadEntityRenderer(new ModelZombie(), 0.5F));
            registerRenderer(EntitySlime.Class, new SlimeEntityRenderer(new ModelSlime(16), new ModelSlime(0), 0.25F));
            registerRenderer(EntityPlayer.Class, new PlayerEntityRenderer());
            registerRenderer(EntityGiantZombie.Class, new GiantEntityRenderer(new ModelZombie(), 0.5F, 6.0F));
            registerRenderer(EntityGhast.Class, new GhastEntityRenderer());
            registerRenderer(EntitySquid.Class, new SquidEntityRenderer(new ModelSquid(), 0.7F));
            registerRenderer(EntityLiving.Class, new LivingEntityRenderer(new ModelBiped(), 0.5F));
            registerRenderer(Entity.Class, new BoxEntityRenderer());
            registerRenderer(EntityPainting.Class, new PaintingEntityRenderer());
            registerRenderer(EntityArrow.Class, new ArrowEntityRenderer());
            registerRenderer(EntitySnowball.Class, new ProjectileEntityRenderer(Item.snowball.getIconFromDamage(0)));
            registerRenderer(EntityEgg.Class, new ProjectileEntityRenderer(Item.egg.getIconFromDamage(0)));
            registerRenderer(EntityFireball.Class, new FireballEntityRenderer());
            registerRenderer(EntityItem.Class, new ItemRenderer());
            registerRenderer(EntityTNTPrimed.Class, new TntEntityRenderer());
            registerRenderer(EntityFallingSand.Class, new FallingBlockEntityRenderer());
            registerRenderer(EntityMinecart.Class, new MinecartEntityRenderer());
            registerRenderer(EntityBoat.Class, new BoatEntityRenderer());
            registerRenderer(EntityFish.Class, new FishingBobberEntityRenderer());
            registerRenderer(EntityLightningBolt.Class, new LightningEntityRenderer());

            foreach (var render in entityRenderMap.Values)
            {
                render.setDispatcher(this);
            }
        }

        private void registerRenderer(Class clazz, EntityRenderer render)
        {
            entityRenderMap[clazz] = render;
        }

        public EntityRenderer getEntityClassRenderObject(Class var1)
        {
            entityRenderMap.TryGetValue(var1, out EntityRenderer? var2);
            if (var2 == null && var1 != Entity.Class)
            {
                var2 = getEntityClassRenderObject(var1.getSuperclass());
                registerRenderer(var1, var2);
            }

            return var2;
        }

        public EntityRenderer getEntityRenderObject(Entity var1)
        {
            return getEntityClassRenderObject(var1.getClass());
        }

        public void cacheActiveRenderInfo(World var1, TextureManager var2, TextRenderer var3, EntityLiving var4, GameOptions var5, float var6)
        {
            world = var1;
            textureManager = var2;
            options = var5;
            cameraEntity = var4;
            fontRenderer = var3;
            if (var4.isSleeping())
            {
                int var7 = var1.getBlockId(MathHelper.floor_double(var4.posX), MathHelper.floor_double(var4.posY), MathHelper.floor_double(var4.posZ));
                if (var7 == Block.BED.id)
                {
                    int var8 = var1.getBlockMeta(MathHelper.floor_double(var4.posX), MathHelper.floor_double(var4.posY), MathHelper.floor_double(var4.posZ));
                    int var9 = var8 & 3;
                    playerViewY = var9 * 90 + 180;
                    playerViewX = 0.0F;
                }
            }
            else
            {
                playerViewY = var4.prevRotationYaw + (var4.rotationYaw - var4.prevRotationYaw) * var6;
                playerViewX = var4.prevRotationPitch + (var4.rotationPitch - var4.prevRotationPitch) * var6;
            }

            x = var4.lastTickPosX + (var4.posX - var4.lastTickPosX) * (double)var6;
            y = var4.lastTickPosY + (var4.posY - var4.lastTickPosY) * (double)var6;
            z = var4.lastTickPosZ + (var4.posZ - var4.lastTickPosZ) * (double)var6;
        }

        public void renderEntity(Entity var1, float var2)
        {
            double var3 = var1.lastTickPosX + (var1.posX - var1.lastTickPosX) * (double)var2;
            double var5 = var1.lastTickPosY + (var1.posY - var1.lastTickPosY) * (double)var2;
            double var7 = var1.lastTickPosZ + (var1.posZ - var1.lastTickPosZ) * (double)var2;
            float var9 = var1.prevRotationYaw + (var1.rotationYaw - var1.prevRotationYaw) * var2;
            float var10 = var1.getEntityBrightness(var2);
            GLManager.GL.Color3(var10, var10, var10);
            renderEntityWithPosYaw(var1, var3 - offsetX, var5 - offsetY, var7 - offsetZ, var9, var2);
        }

        public void renderEntityWithPosYaw(Entity var1, double var2, double var4, double var6, float var8, float var9)
        {
            EntityRenderer var10 = getEntityRenderObject(var1);
            if (var10 != null)
            {
                var10.render(var1, var2, var4, var6, var8, var9);
                var10.postRender(var1, var2, var4, var6, var8, var9);
            }

        }

        public void func_852_a(World var1)
        {
            world = var1;
        }

        public double squareDistanceTo(double var1, double var3, double var5)
        {
            double var7 = var1 - x;
            double var9 = var3 - y;
            double var11 = var5 - z;
            return var7 * var7 + var9 * var9 + var11 * var11;
        }

        public TextRenderer getTextRenderer()
        {
            return fontRenderer;
        }
    }

}