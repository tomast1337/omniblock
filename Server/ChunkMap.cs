using betareborn.Blocks;
using betareborn.Blocks.Entities;
using betareborn.Entities;
using betareborn.Network.Packets;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.Server
{
    public class ChunkMap
    {
        public List players = new ArrayList();
        private LongObjectHashMap chunkMapping = new LongObjectHashMap();
        private List chunksToUpdate = new ArrayList();
        private MinecraftServer server;
        private int dimensionId;
        private int viewDistance;
        private readonly int[][] DIRECTIONS = new int[][] { { 1, 0 }, { 0, 1 }, { -1, 0 }, { 0, -1 } };

        public ChunkMap(MinecraftServer server, int dimensionId, int viewRadius)
        {
            if (viewRadius > 15)
            {
                throw new IllegalArgumentException("Too big view radius!");
            }
            else if (viewRadius < 3)
            {
                throw new IllegalArgumentException("Too small view radius!");
            }
            else
            {
                this.viewDistance = viewRadius;
                this.server = server;
                this.dimensionId = dimensionId;
            }
        }

        public ServerWorld getWorld()
        {
            return this.server.getWorld(this.dimensionId);
        }

        public void updateChunks()
        {
            for (int var1 = 0; var1 < this.chunksToUpdate.size(); var1++)
            {
                ((ChunkMap__TrackedChunk)this.chunksToUpdate.get(var1)).updateChunk();
            }

            this.chunksToUpdate.clear();
        }

        private ChunkMap__TrackedChunk getOrCreateChunk(int chunkX, int chunkZ, boolean createIfAbsent)
        {
            long var4 = chunkX + 2147483647L | chunkZ + 2147483647L << 32;
            ChunkMap__TrackedChunk var6 = (ChunkMap__TrackedChunk)this.chunkMapping.get(var4);
            if (var6 == null && createIfAbsent)
            {
                var6 = new ChunkMap__TrackedChunk(this, chunkX, chunkZ);
                this.chunkMapping.put(var4, var6);
            }

            return var6;
        }

        public void markBlockForUpdate(int x, int y, int z)
        {
            int var4 = x >> 4;
            int var5 = z >> 4;
            ChunkMap__TrackedChunk var6 = this.getOrCreateChunk(var4, var5, false);
            if (var6 != null)
            {
                var6.updatePlayerChunks(x & 15, y, z & 15);
            }
        }

        public void addPlayer(ServerPlayerEntity player)
        {
            int var2 = (int)player.x >> 4;
            int var3 = (int)player.z >> 4;
            player.lastX = player.x;
            player.lastZ = player.z;
            int var4 = 0;
            int var5 = this.viewDistance;
            int var6 = 0;
            int var7 = 0;
            this.getOrCreateChunk(var2, var3, true).addPlayer(player);

            for (int var8 = 1; var8 <= var5 * 2; var8++)
            {
                for (int var9 = 0; var9 < 2; var9++)
                {
                    int[] var10 = this.DIRECTIONS[var4++ % 4];

                    for (int var11 = 0; var11 < var8; var11++)
                    {
                        var6 += var10[0];
                        var7 += var10[1];
                        this.getOrCreateChunk(var2 + var6, var3 + var7, true).addPlayer(player);
                    }
                }
            }

            var4 %= 4;

            for (int var13 = 0; var13 < var5 * 2; var13++)
            {
                var6 += this.DIRECTIONS[var4][0];
                var7 += this.DIRECTIONS[var4][1];
                this.getOrCreateChunk(var2 + var6, var3 + var7, true).addPlayer(player);
            }

            this.players.add(player);
        }

        public void removePlayer(ServerPlayerEntity player)
        {
            int var2 = (int)player.lastX >> 4;
            int var3 = (int)player.lastZ >> 4;

            for (int var4 = var2 - this.viewDistance; var4 <= var2 + this.viewDistance; var4++)
            {
                for (int var5 = var3 - this.viewDistance; var5 <= var3 + this.viewDistance; var5++)
                {
                    ChunkMap__TrackedChunk var6 = this.getOrCreateChunk(var4, var5, false);
                    if (var6 != null)
                    {
                        var6.removePlayer(player);
                    }
                }
            }

            this.players.remove(player);
        }

        private boolean isWithinViewDistance(int chunkX, int chunkZ, int centerX, int centerZ)
        {
            int var5 = chunkX - centerX;
            int var6 = chunkZ - centerZ;
            return var5 < -this.viewDistance || var5 > this.viewDistance ? false : var6 >= -this.viewDistance && var6 <= this.viewDistance;
        }

        public void updatePlayerChunks(ServerPlayerEntity player)
        {
            int var2 = (int)player.x >> 4;
            int var3 = (int)player.z >> 4;
            double var4 = player.lastX - player.x;
            double var6 = player.lastZ - player.z;
            double var8 = var4 * var4 + var6 * var6;
            if (!(var8 < 64.0))
            {
                int var10 = (int)player.lastX >> 4;
                int var11 = (int)player.lastZ >> 4;
                int var12 = var2 - var10;
                int var13 = var3 - var11;
                if (var12 != 0 || var13 != 0)
                {
                    for (int var14 = var2 - this.viewDistance; var14 <= var2 + this.viewDistance; var14++)
                    {
                        for (int var15 = var3 - this.viewDistance; var15 <= var3 + this.viewDistance; var15++)
                        {
                            if (!this.isWithinViewDistance(var14, var15, var10, var11))
                            {
                                this.getOrCreateChunk(var14, var15, true).addPlayer(player);
                            }

                            if (!this.isWithinViewDistance(var14 - var12, var15 - var13, var2, var3))
                            {
                                ChunkMap__TrackedChunk var16 = this.getOrCreateChunk(var14 - var12, var15 - var13, false);
                                if (var16 != null)
                                {
                                    var16.removePlayer(player);
                                }
                            }
                        }
                    }

                    player.lastX = player.x;
                    player.lastZ = player.z;
                }
            }
        }

        public int getBlockViewDistance()
        {
            return this.viewDistance * 16 - 16;
        }

        private class TrackedChunk
        {
            private readonly ChunkMap chunkMap;
            private List players;
            private int chunkX;
            private int chunkZ;
            private ChunkPos chunkPos;
            private short[] dirtyBlocks;
            private int dirtyBlockCount;
            private int minX;
            private int minY;
            private int minZ;
            private int maxX;
            private int maxY;
            private int maxZ;

            public TrackedChunk(ChunkMap chunkMap, int chunkX, int chunkY)
            {
                this.chunkMap = chunkMap;
                this.players = new ArrayList();
                this.dirtyBlocks = new short[10];
                this.dirtyBlockCount = 0;
                this.chunkX = chunkX;
                this.chunkZ = chunkY;
                this.chunkPos = new ChunkPos(chunkX, chunkY);
                chunkMap.getWorld().chunkCache.loadChunk(chunkX, chunkY);
            }

            public void addPlayer(ServerPlayerEntity player)
            {
                if (this.players.contains(player))
                {
                    throw new IllegalStateException("Failed to add player. " + player + " already is in chunk " + this.chunkX + ", " + this.chunkZ);
                }
                else
                {
                    player.activeChunks.add(this.chunkPos);
                    player.networkHandler.sendPacket(new ChunkStatusUpdateS2CPacket(this.chunkPos.x, this.chunkPos.z, true));
                    this.players.add(player);
                    player.pendingChunkUpdates.add(this.chunkPos);
                }
            }

            public void removePlayer(ServerPlayerEntity player)
            {
                if (this.players.contains(player))
                {
                    this.players.remove(player);
                    if (this.players.size() == 0)
                    {
                        long var2 = this.chunkX + 2147483647L | this.chunkZ + 2147483647L << 32;
                        ChunkMap.m_37805459(this.f_46746473).remove(var2);
                        if (this.dirtyBlockCount > 0)
                        {
                            ChunkMap.m_79544610(this.f_46746473).remove(this);
                        }

                        chunkMap.getWorld().chunkCache.isLoaded(this.chunkX, this.chunkZ);
                    }

                    player.pendingChunkUpdates.remove(this.chunkPos);
                    if (player.activeChunks.contains(this.chunkPos))
                    {
                        player.networkHandler.sendPacket(new ChunkStatusUpdateS2CPacket(this.chunkX, this.chunkZ, false));
                    }
                }
            }

            public void updatePlayerChunks(int x, int y, int z)
            {
                if (this.dirtyBlockCount == 0)
                {
                    ChunkMap.m_79544610(this.f_46746473).add(this);
                    this.minX = this.minY = x;
                    this.minZ = this.maxX = y;
                    this.maxY = this.maxZ = z;
                }

                if (this.minX > x)
                {
                    this.minX = x;
                }

                if (this.minY < x)
                {
                    this.minY = x;
                }

                if (this.minZ > y)
                {
                    this.minZ = y;
                }

                if (this.maxX < y)
                {
                    this.maxX = y;
                }

                if (this.maxY > z)
                {
                    this.maxY = z;
                }

                if (this.maxZ < z)
                {
                    this.maxZ = z;
                }

                if (this.dirtyBlockCount < 10)
                {
                    short var4 = (short)(x << 12 | z << 8 | y);

                    for (int var5 = 0; var5 < this.dirtyBlockCount; var5++)
                    {
                        if (this.dirtyBlocks[var5] == var4)
                        {
                            return;
                        }
                    }

                    this.dirtyBlocks[this.dirtyBlockCount++] = var4;
                }
            }

            public void sendPacketToPlayers(Packet packet)
            {
                for (int var2 = 0; var2 < this.players.size(); var2++)
                {
                    ServerPlayerEntity var3 = (ServerPlayerEntity)this.players.get(var2);
                    if (var3.activeChunks.contains(this.chunkPos))
                    {
                        var3.networkHandler.sendPacket(packet);
                    }
                }
            }

            public void updateChunk()
            {
                ServerWorld var1 = chunkMap.getWorld();
                if (this.dirtyBlockCount != 0)
                {
                    if (this.dirtyBlockCount == 1)
                    {
                        int var2 = this.chunkX * 16 + this.minX;
                        int var3 = this.minZ;
                        int var4 = this.chunkZ * 16 + this.maxY;
                        this.sendPacketToPlayers(new BlockUpdateS2CPacket(var2, var3, var4, var1));
                        if (Block.BLOCKS_WITH_ENTITY[var1.getBlockId(var2, var3, var4)])
                        {
                            this.sendBlockEntityUpdate(var1.getBlockEntity(var2, var3, var4));
                        }
                    }
                    else if (this.dirtyBlockCount == 10)
                    {
                        this.minZ = this.minZ / 2 * 2;
                        this.maxX = (this.maxX / 2 + 1) * 2;
                        int var10 = this.minX + this.chunkX * 16;
                        int var12 = this.minZ;
                        int var14 = this.maxY + this.chunkZ * 16;
                        int var5 = this.minY - this.minX + 1;
                        int var6 = this.maxX - this.minZ + 2;
                        int var7 = this.maxZ - this.maxY + 1;
                        this.sendPacketToPlayers(new ChunkDataS2CPacket(var10, var12, var14, var5, var6, var7, var1));
                        List var8 = var1.getBlockEntities(var10, var12, var14, var10 + var5, var12 + var6, var14 + var7);

                        for (int var9 = 0; var9 < var8.size(); var9++)
                        {
                            this.sendBlockEntityUpdate((BlockEntity)var8.get(var9));
                        }
                    }
                    else
                    {
                        this.sendPacketToPlayers(new ChunkDeltaUpdateS2CPacket(this.chunkX, this.chunkZ, this.dirtyBlocks, this.dirtyBlockCount, var1));

                        for (int var11 = 0; var11 < this.dirtyBlockCount; var11++)
                        {
                            int var13 = this.chunkX * 16 + (this.dirtyBlockCount >> 12 & 15);
                            int var15 = this.dirtyBlockCount & 0xFF;
                            int var16 = this.chunkZ * 16 + (this.dirtyBlockCount >> 8 & 15);
                            if (Block.BLOCKS_WITH_ENTITY[var1.getBlockId(var13, var15, var16)])
                            {
                                java.lang.System.@out.println("Sending!");
                                this.sendBlockEntityUpdate(var1.getBlockEntity(var13, var15, var16));
                            }
                        }
                    }

                    this.dirtyBlockCount = 0;
                }
            }

            private void sendBlockEntityUpdate(BlockEntity blockentity)
            {
                if (blockentity != null)
                {
                    Packet var2 = blockentity.createUpdatePacket();
                    if (var2 != null)
                    {
                        this.sendPacketToPlayers(var2);
                    }
                }
            }
        }
    }

}
