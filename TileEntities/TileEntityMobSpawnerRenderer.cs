using betareborn.Entities;
using betareborn.Rendering;
using betareborn.Worlds;
using java.util;

namespace betareborn.TileEntities
{
    public class TileEntityMobSpawnerRenderer : TileEntitySpecialRenderer
    {

        private Map entityHashMap = new HashMap();

        public void renderTileEntityMobSpawner(TileEntityMobSpawner var1, double var2, double var4, double var6, float var8)
        {
            GLManager.GL.PushMatrix();
            GLManager.GL.Translate((float)var2 + 0.5F, (float)var4, (float)var6 + 0.5F);
            Entity var9 = (Entity)entityHashMap.get(var1.getSpawnedEntityId());
            if (var9 == null)
            {
                var9 = EntityList.createEntityInWorld(var1.getSpawnedEntityId(), (World)null);
                entityHashMap.put(var1.getSpawnedEntityId(), var9);
            }

            if (var9 != null)
            {
                var9.setWorld(var1.world);
                float var10 = 7.0F / 16.0F;
                GLManager.GL.Translate(0.0F, 0.4F, 0.0F);
                GLManager.GL.Rotate((float)(var1.lastRotation + (var1.rotation - var1.lastRotation) * (double)var8) * 10.0F, 0.0F, 1.0F, 0.0F);
                GLManager.GL.Rotate(-30.0F, 1.0F, 0.0F, 0.0F);
                GLManager.GL.Translate(0.0F, -0.4F, 0.0F);
                GLManager.GL.Scale(var10, var10, var10);
                var9.setLocationAndAngles(var2, var4, var6, 0.0F, 0.0F);
                RenderManager.instance.renderEntityWithPosYaw(var9, 0.0D, 0.0D, 0.0D, 0.0F, var8);
            }

            GLManager.GL.PopMatrix();
        }

        public override void renderTileEntityAt(TileEntity var1, double var2, double var4, double var6, float var8)
        {
            renderTileEntityMobSpawner((TileEntityMobSpawner)var1, var2, var4, var6, var8);
        }
    }

}