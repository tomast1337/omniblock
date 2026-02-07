using betareborn.Entities;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityMobSpawner : TileEntity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntityMobSpawner).TypeHandle);

        public int spawnDelay = -1;
        private string spawnedEntityId = "Pig";
        public double rotation;
        public double lastRotation = 0.0D;

        public TileEntityMobSpawner()
        {
            spawnDelay = 20;
        }

        public string getSpawnedEntityId()
        {
            return spawnedEntityId;
        }

        public void setSpawnedEntityId(string spawnedEntityId)
        {
            this.spawnedEntityId = spawnedEntityId;
        }

        public bool isPlayerInRange()
        {
            return world.getClosestPlayer((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, 16.0D) != null;
        }

        public override void tick()
        {
            lastRotation = rotation;
            if (isPlayerInRange())
            {
                double var1 = (double)((float)x + world.random.nextFloat());
                double var3 = (double)((float)y + world.random.nextFloat());
                double var5 = (double)((float)z + world.random.nextFloat());
                world.addParticle("smoke", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var1, var3, var5, 0.0D, 0.0D, 0.0D);

                for (rotation += (double)(1000.0F / ((float)spawnDelay + 200.0F)); rotation > 360.0D; lastRotation -= 360.0D)
                {
                    rotation -= 360.0D;
                }

                if (!world.multiplayerWorld)
                {
                    if (spawnDelay == -1)
                    {
                        resetDelay();
                    }

                    if (spawnDelay > 0)
                    {
                        --spawnDelay;
                        return;
                    }

                    byte var7 = 4;

                    for (int var8 = 0; var8 < var7; ++var8)
                    {
                        EntityLiving var9 = (EntityLiving)((EntityLiving)EntityRegistry.create(spawnedEntityId, world));
                        if (var9 == null)
                        {
                            return;
                        }

                        int var10 = world.getEntitiesWithinAABB(var9.getClass(), Box.createCached((double)x, (double)y, (double)z, (double)(x + 1), (double)(y + 1), (double)(z + 1)).expand(8.0D, 4.0D, 8.0D)).Count;
                        if (var10 >= 6)
                        {
                            resetDelay();
                            return;
                        }

                        if (var9 != null)
                        {
                            double var11 = (double)x + (world.random.nextDouble() - world.random.nextDouble()) * 4.0D;
                            double var13 = (double)(y + world.random.nextInt(3) - 1);
                            double var15 = (double)z + (world.random.nextDouble() - world.random.nextDouble()) * 4.0D;
                            var9.setPositionAndAnglesKeepPrevAngles(var11, var13, var15, world.random.nextFloat() * 360.0F, 0.0F);
                            if (var9.canSpawn())
                            {
                                world.spawnEntity(var9);

                                for (int var17 = 0; var17 < 20; ++var17)
                                {
                                    var1 = (double)x + 0.5D + ((double)world.random.nextFloat() - 0.5D) * 2.0D;
                                    var3 = (double)y + 0.5D + ((double)world.random.nextFloat() - 0.5D) * 2.0D;
                                    var5 = (double)z + 0.5D + ((double)world.random.nextFloat() - 0.5D) * 2.0D;
                                    world.addParticle("smoke", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                                    world.addParticle("flame", var1, var3, var5, 0.0D, 0.0D, 0.0D);
                                }

                                var9.animateSpawn();
                                resetDelay();
                            }
                        }
                    }
                }

                base.tick();
            }
        }

        private void resetDelay()
        {
            spawnDelay = 200 + world.random.nextInt(600);
        }

        public override void readNbt(NBTTagCompound nbt)
        {
            base.readNbt(nbt);
            spawnedEntityId = nbt.getString("EntityId");
            spawnDelay = nbt.getShort("Delay");
        }

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            nbt.setString("EntityId", spawnedEntityId);
            nbt.setShort("Delay", (short)spawnDelay);
        }
    }

}