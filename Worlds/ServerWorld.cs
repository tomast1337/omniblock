using betareborn.Blocks.Entities;
using betareborn.Entities;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Server.Worlds;
using betareborn.Util.Maths;
using betareborn.Worlds.Chunks;
using betareborn.Worlds.Chunks.Storage;
using betareborn.Worlds.Dimensions;
using betareborn.Worlds.Storage;

namespace betareborn.Worlds
{
    public class ServerWorld : World
    {
        public ServerChunkCache chunkCache;
        public bool bypassSpawnProtection = false;
        public bool savingDisabled;
        private MinecraftServer server;
        private Dictionary<int, Entity> entitiesById = [];

        public ServerWorld(MinecraftServer server, WorldStorage storage, String name, int dimensionId, long seed) : base(storage, name, seed, Dimension.fromId(dimensionId))
        {
            this.server = server;
        }


        public override void updateEntity(Entity entity, bool requireLoaded)
        {
            if (!server.spawnAnimals && (entity is EntityAnimal || entity is EntityWaterMob))
            {
                entity.markDead();
            }

            if (entity.passenger == null || !(entity.passenger is EntityPlayer))
            {
                base.updateEntity(entity, requireLoaded);
            }
        }

        public void tickVehicle(Entity vehicle, bool requireLoaded)
        {
            base.updateEntity(vehicle, requireLoaded);
        }


        protected override ChunkSource createChunkCache()
        {
            ChunkStorage var1 = storage.getChunkStorage(dimension);
            chunkCache = new ServerChunkCache(this, var1, dimension.createChunkGenerator());
            return chunkCache;
        }

        public java.util.List getBlockEntities(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            java.util.ArrayList var7 = new java.util.ArrayList();

            for (int var8 = 0; var8 < blockEntities.Capacity; var8++)
            {
                BlockEntity var9 = blockEntities[var8];
                if (var9.x >= minX && var9.y >= minY && var9.z >= minZ && var9.x < maxX && var9.y < maxY && var9.z < maxZ)
                {
                    var7.add(var9);
                }
            }

            return var7;
        }


        public override bool canInteract(EntityPlayer player, int x, int y, int z)
        {
            int var5 = (int)MathHelper.abs(x - properties.getSpawnX());
            int var6 = (int)MathHelper.abs(z - properties.getSpawnZ());
            if (var5 > var6)
            {
                var6 = var5;
            }

            return var6 > 16 || server.playerManager.isOperator(player.name);
        }


        protected override void notifyEntityAdded(Entity entity)
        {
            base.notifyEntityAdded(entity);
            entitiesById.Add(entity.id, entity);
        }


        protected override void notifyEntityRemoved(Entity entity)
        {
            base.notifyEntityRemoved(entity);
            entitiesById.Remove(entity.id);
        }

        public Entity getEntity(int id)
        {
            entitiesById.TryGetValue(id, out Entity? entity);
            return entity;
        }


        public override bool spawnGlobalEntity(Entity entity)
        {
            if (base.spawnGlobalEntity(entity))
            {
                server.playerManager.sendToAround(entity.x, entity.y, entity.z, 512.0, dimension.id, new GlobalEntitySpawnS2CPacket(entity));
                return true;
            }
            else
            {
                return false;
            }
        }


        public override void broadcastEntityEvent(Entity entity, byte @event)
        {
            EntityStatusS2CPacket var3 = new EntityStatusS2CPacket(entity.id, @event);
            server.getEntityTracker(dimension.id).sendToAround(entity, var3);
        }


        public override Explosion createExplosion(Entity source, double x, double y, double z, float power, bool fire)
        {
            Explosion var10 = new Explosion(this, source, x, y, z, power)
            {
                isFlaming = fire
            };
            var10.doExplosionA();
            var10.doExplosionB(false);
            server.playerManager.sendToAround(x, y, z, 64.0, dimension.id, new ExplosionS2CPacket(x, y, z, power, var10.destroyedBlockPositions));
            return var10;
        }


        public override void playNoteBlockActionAt(int x, int y, int z, int soundType, int pitch)
        {
            base.playNoteBlockActionAt(x, y, z, soundType, pitch);
            server.playerManager.sendToAround(x, y, z, 64.0, dimension.id, new PlayNoteSoundS2CPacket(x, y, z, soundType, pitch));
        }

        public void forceSave()
        {
            storage.forceSave();
        }


        protected override void updateWeatherCycles()
        {
            bool var1 = isRaining();
            base.updateWeatherCycles();
            if (var1 != isRaining())
            {
                if (var1)
                {
                    server.playerManager.sendToAll(new GameStateChangeS2CPacket(2));
                }
                else
                {
                    server.playerManager.sendToAll(new GameStateChangeS2CPacket(1));
                }
            }
        }
    }
}
