using betareborn.Entities;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityMobSpawner : TileEntity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntityMobSpawner).TypeHandle);

        public int delay = -1;
        private string mobID = "Pig";
        public double yaw;
        public double yaw2 = 0.0D;

        public TileEntityMobSpawner()
        {
            delay = 20;
        }

        public string getMobID()
        {
            return mobID;
        }

        public void setMobID(string var1)
        {
            mobID = var1;
        }

        public bool anyPlayerInRange()
        {
            return world.getClosestPlayer((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, 16.0D) != null;
        }

        public override void tick()
        {
            yaw2 = yaw;
            if (anyPlayerInRange())
            {
                double var1 = (double)((float)x + world.rand.nextFloat());
                double var3 = (double)((float)y + world.rand.nextFloat());
                double var5 = (double)((float)z + world.rand.nextFloat());
                world.spawnParticle("smoke", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                world.spawnParticle("flame", var1, var3, var5, 0.0D, 0.0D, 0.0D);

                for (yaw += (double)(1000.0F / ((float)delay + 200.0F)); yaw > 360.0D; yaw2 -= 360.0D)
                {
                    yaw -= 360.0D;
                }

                if (!world.multiplayerWorld)
                {
                    if (delay == -1)
                    {
                        updateDelay();
                    }

                    if (delay > 0)
                    {
                        --delay;
                        return;
                    }

                    byte var7 = 4;

                    for (int var8 = 0; var8 < var7; ++var8)
                    {
                        EntityLiving var9 = (EntityLiving)((EntityLiving)EntityList.createEntityInWorld(mobID, world));
                        if (var9 == null)
                        {
                            return;
                        }

                        int var10 = world.getEntitiesWithinAABB(var9.getClass(), AxisAlignedBB.getBoundingBoxFromPool((double)x, (double)y, (double)z, (double)(x + 1), (double)(y + 1), (double)(z + 1)).expand(8.0D, 4.0D, 8.0D)).Count;
                        if (var10 >= 6)
                        {
                            updateDelay();
                            return;
                        }

                        if (var9 != null)
                        {
                            double var11 = (double)x + (world.rand.nextDouble() - world.rand.nextDouble()) * 4.0D;
                            double var13 = (double)(y + world.rand.nextInt(3) - 1);
                            double var15 = (double)z + (world.rand.nextDouble() - world.rand.nextDouble()) * 4.0D;
                            var9.setLocationAndAngles(var11, var13, var15, world.rand.nextFloat() * 360.0F, 0.0F);
                            if (var9.getCanSpawnHere())
                            {
                                world.entityJoinedWorld(var9);

                                for (int var17 = 0; var17 < 20; ++var17)
                                {
                                    var1 = (double)x + 0.5D + ((double)world.rand.nextFloat() - 0.5D) * 2.0D;
                                    var3 = (double)y + 0.5D + ((double)world.rand.nextFloat() - 0.5D) * 2.0D;
                                    var5 = (double)z + 0.5D + ((double)world.rand.nextFloat() - 0.5D) * 2.0D;
                                    world.spawnParticle("smoke", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                                    world.spawnParticle("flame", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                                }

                                var9.spawnExplosionParticle();
                                updateDelay();
                            }
                        }
                    }
                }

                base.tick();
            }
        }

        private void updateDelay()
        {
            delay = 200 + world.rand.nextInt(600);
        }

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            mobID = var1.getString("EntityId");
            delay = var1.getShort("Delay");
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            var1.setString("EntityId", mobID);
            var1.setShort("Delay", (short)delay);
        }
    }

}