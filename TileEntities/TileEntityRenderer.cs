using betareborn.Entities;
using betareborn.Rendering;
using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.TileEntities
{
    public class TileEntityRenderer
    {
        private Map specialRendererMap = new HashMap();
        public static TileEntityRenderer instance = new TileEntityRenderer();
        private FontRenderer fontRenderer;
        public static double staticPlayerX;
        public static double staticPlayerY;
        public static double staticPlayerZ;
        public RenderEngine renderEngine;
        public World worldObj;
        public EntityLiving entityLivingPlayer;
        public float playerYaw;
        public float playerPitch;
        public double playerX;
        public double playerY;
        public double playerZ;

        private TileEntityRenderer()
        {
            specialRendererMap.put(TileEntitySign.Class, new TileEntitySignRenderer());
            specialRendererMap.put(TileEntityMobSpawner.Class, new TileEntityMobSpawnerRenderer());
            specialRendererMap.put(TileEntityPiston.Class, new TileEntityRendererPiston());
            Iterator var1 = specialRendererMap.values().iterator();

            while (var1.hasNext())
            {
                TileEntitySpecialRenderer var2 = (TileEntitySpecialRenderer)var1.next();
                var2.setTileEntityRenderer(this);
            }

        }

        public TileEntitySpecialRenderer getSpecialRendererForClass(Class var1)
        {
            TileEntitySpecialRenderer var2 = (TileEntitySpecialRenderer)specialRendererMap.get(var1);
            if (var2 == null && var1 != TileEntity.Class)
            {
                var2 = getSpecialRendererForClass(var1.getSuperclass());
                specialRendererMap.put(var1, var2);
            }

            return var2;
        }

        public bool hasSpecialRenderer(TileEntity var1)
        {
            return getSpecialRendererForEntity(var1) != null;
        }

        public TileEntitySpecialRenderer getSpecialRendererForEntity(TileEntity var1)
        {
            return var1 == null ? null : getSpecialRendererForClass(var1.getClass());
        }

        public void cacheActiveRenderInfo(World var1, RenderEngine var2, FontRenderer var3, EntityLiving var4, float var5)
        {
            if (worldObj != var1)
            {
                func_31072_a(var1);
            }

            renderEngine = var2;
            entityLivingPlayer = var4;
            fontRenderer = var3;
            playerYaw = var4.prevRotationYaw + (var4.rotationYaw - var4.prevRotationYaw) * var5;
            playerPitch = var4.prevRotationPitch + (var4.rotationPitch - var4.prevRotationPitch) * var5;
            playerX = var4.lastTickPosX + (var4.posX - var4.lastTickPosX) * (double)var5;
            playerY = var4.lastTickPosY + (var4.posY - var4.lastTickPosY) * (double)var5;
            playerZ = var4.lastTickPosZ + (var4.posZ - var4.lastTickPosZ) * (double)var5;
        }

        public void renderTileEntity(TileEntity var1, float var2)
        {
            if (var1.distanceFrom(playerX, playerY, playerZ) < 4096.0D)
            {
                float var3 = worldObj.getLightBrightness(var1.x, var1.y, var1.z);
                GLManager.GL.Color3(var3, var3, var3);
                renderTileEntityAt(var1, (double)var1.x - staticPlayerX, (double)var1.y - staticPlayerY, (double)var1.z - staticPlayerZ, var2);
            }

        }

        public void renderTileEntityAt(TileEntity var1, double var2, double var4, double var6, float var8)
        {
            TileEntitySpecialRenderer var9 = getSpecialRendererForEntity(var1);
            if (var9 != null)
            {
                var9.renderTileEntityAt(var1, var2, var4, var6, var8);
            }

        }

        public void func_31072_a(World var1)
        {
            worldObj = var1;
            Iterator var2 = specialRendererMap.values().iterator();

            while (var2.hasNext())
            {
                TileEntitySpecialRenderer var3 = (TileEntitySpecialRenderer)var2.next();
                if (var3 != null)
                {
                    var3.func_31069_a(var1);
                }
            }

        }

        public FontRenderer getFontRenderer()
        {
            return fontRenderer;
        }
    }

}