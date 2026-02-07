using betareborn.Chunks;
using betareborn.Entities;
using betareborn.Packets;
using java.util;

namespace betareborn.Worlds
{
    public class WorldClient : World
    {

        private LinkedList field_1057_z = new LinkedList();
        private NetClientHandler sendQueue;
        private ChunkProviderClient field_20915_C;
        private MCHash field_1055_D = new MCHash();
        private Set field_20914_E = new HashSet();
        private Set field_1053_F = new HashSet();

        public WorldClient(NetClientHandler var1, long var2, int var4) : base(new SaveHandlerMP(), "MpServer", WorldProvider.getProviderForDimension(var4), var2)
        {
            sendQueue = var1;
            setSpawnPoint(new ChunkCoordinates(8, 64, 8));
            field_28108_z = var1.field_28118_b;
        }

        public override void tick(int _)
        {
            setWorldTime(getWorldTime() + 1L);
            int var1 = calculateSkylightSubtracted(1.0F);
            int var2;
            if (var1 != skylightSubtracted)
            {
                skylightSubtracted = var1;

                for (var2 = 0; var2 < worldAccesses.Count; ++var2)
                {
                    worldAccesses[var2].updateAllRenderers();
                }
            }

            for (var2 = 0; var2 < 10 && !field_1053_F.isEmpty(); ++var2)
            {
                Entity var3 = (Entity)field_1053_F.iterator().next();
                if (!loadedEntityList.Contains(var3))
                {
                    spawnEntity(var3);
                }
            }

            sendQueue.processReadPackets();

            for (var2 = 0; var2 < field_1057_z.size(); ++var2)
            {
                WorldBlockPositionType var4 = (WorldBlockPositionType)field_1057_z.get(var2);
                if (--var4.field_1206_d == 0)
                {
                    base.setBlockAndMetadata(var4.field_1202_a, var4.field_1201_b, var4.field_1207_c, var4.field_1205_e, var4.field_1204_f);
                    base.markBlockNeedsUpdate(var4.field_1202_a, var4.field_1201_b, var4.field_1207_c);
                    field_1057_z.remove(var2--);
                }
            }

        }

        public void func_711_c(int var1, int var2, int var3, int var4, int var5, int var6)
        {
            for (int var7 = 0; var7 < field_1057_z.size(); ++var7)
            {
                WorldBlockPositionType var8 = (WorldBlockPositionType)field_1057_z.get(var7);
                if (var8.field_1202_a >= var1 && var8.field_1201_b >= var2 && var8.field_1207_c >= var3 && var8.field_1202_a <= var4 && var8.field_1201_b <= var5 && var8.field_1207_c <= var6)
                {
                    field_1057_z.remove(var7--);
                }
            }

        }

        protected override IChunkProvider getChunkProvider()
        {
            field_20915_C = new ChunkProviderClient(this);
            return field_20915_C;
        }

        public override void setSpawnLocation()
        {
            setSpawnPoint(new ChunkCoordinates(8, 64, 8));
        }

        protected override void updateBlocksAndPlayCaveSounds()
        {
        }

        public override void scheduleBlockUpdate(int var1, int var2, int var3, int var4, int var5)
        {
        }

        public override bool TickUpdates(bool var1)
        {
            return false;
        }

        public void doPreChunk(int var1, int var2, bool var3)
        {
            if (var3)
            {
                field_20915_C.prepareChunk(var1, var2);
            }
            else
            {
                field_20915_C.func_539_c(var1, var2);
            }

            if (!var3)
            {
                markBlocksDirty(var1 * 16, 0, var2 * 16, var1 * 16 + 15, 128, var2 * 16 + 15);
            }

        }

        public override bool spawnEntity(Entity var1)
        {
            bool var2 = base.spawnEntity(var1);
            field_20914_E.add(var1);
            if (!var2)
            {
                field_1053_F.add(var1);
            }

            return var2;
        }

        public override void setEntityDead(Entity var1)
        {
            base.setEntityDead(var1);
            field_20914_E.remove(var1);
        }

        protected override void obtainEntitySkin(Entity var1)
        {
            base.obtainEntitySkin(var1);
            if (field_1053_F.contains(var1))
            {
                field_1053_F.remove(var1);
            }

        }

        protected override void releaseEntitySkin(Entity var1)
        {
            base.releaseEntitySkin(var1);
            if (field_20914_E.contains(var1))
            {
                field_1053_F.add(var1);
            }

        }

        public void func_712_a(int var1, Entity var2)
        {
            Entity var3 = func_709_b(var1);
            if (var3 != null)
            {
                setEntityDead(var3);
            }

            field_20914_E.add(var2);
            var2.entityId = var1;
            if (!spawnEntity(var2))
            {
                field_1053_F.add(var2);
            }

            field_1055_D.addKey(var1, var2);
        }

        public Entity func_709_b(int var1)
        {
            return (Entity)field_1055_D.lookup(var1);
        }

        public Entity removeEntityFromWorld(int var1)
        {
            Entity var2 = (Entity)field_1055_D.removeObject(var1);
            if (var2 != null)
            {
                field_20914_E.remove(var2);
                setEntityDead(var2);
            }

            return var2;
        }

        public override bool setBlockMetadata(int var1, int var2, int var3, int var4)
        {
            int var5 = getBlockId(var1, var2, var3);
            int var6 = getBlockMetadata(var1, var2, var3);
            if (base.setBlockMetadata(var1, var2, var3, var4))
            {
                field_1057_z.add(new WorldBlockPositionType(this, var1, var2, var3, var5, var6));
                return true;
            }
            else
            {
                return false;
            }
        }

        public override bool setBlockAndMetadata(int var1, int var2, int var3, int var4, int var5)
        {
            int var6 = getBlockId(var1, var2, var3);
            int var7 = getBlockMetadata(var1, var2, var3);
            if (base.setBlockAndMetadata(var1, var2, var3, var4, var5))
            {
                field_1057_z.add(new WorldBlockPositionType(this, var1, var2, var3, var6, var7));
                return true;
            }
            else
            {
                return false;
            }
        }

        public override bool setBlock(int var1, int var2, int var3, int var4)
        {
            int var5 = getBlockId(var1, var2, var3);
            int var6 = getBlockMetadata(var1, var2, var3);
            if (base.setBlock(var1, var2, var3, var4))
            {
                field_1057_z.add(new WorldBlockPositionType(this, var1, var2, var3, var5, var6));
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool func_714_c(int var1, int var2, int var3, int var4, int var5)
        {
            func_711_c(var1, var2, var3, var1, var2, var3);
            if (base.setBlockAndMetadata(var1, var2, var3, var4, var5))
            {
                notifyBlockChange(var1, var2, var3, var4);
                return true;
            }
            else
            {
                return false;
            }
        }

        public override void sendQuittingDisconnectingPacket()
        {
            sendQueue.func_28117_a(new Packet255KickDisconnect("Quitting"));
        }

        protected override void updateWeather()
        {
            if (!worldProvider.hasNoSky)
            {
                if (field_27168_F > 0)
                {
                    --field_27168_F;
                }

                prevRainingStrength = rainingStrength;
                if (worldInfo.getRaining())
                {
                    rainingStrength = (float)((double)rainingStrength + 0.01D);
                }
                else
                {
                    rainingStrength = (float)((double)rainingStrength - 0.01D);
                }

                if (rainingStrength < 0.0F)
                {
                    rainingStrength = 0.0F;
                }

                if (rainingStrength > 1.0F)
                {
                    rainingStrength = 1.0F;
                }

                prevThunderingStrength = thunderingStrength;
                if (worldInfo.getThundering())
                {
                    thunderingStrength = (float)((double)thunderingStrength + 0.01D);
                }
                else
                {
                    thunderingStrength = (float)((double)thunderingStrength - 0.01D);
                }

                if (thunderingStrength < 0.0F)
                {
                    thunderingStrength = 0.0F;
                }

                if (thunderingStrength > 1.0F)
                {
                    thunderingStrength = 1.0F;
                }

            }
        }
    }

}