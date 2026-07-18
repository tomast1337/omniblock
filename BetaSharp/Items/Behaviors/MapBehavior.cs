using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Maps;

namespace BetaSharp.Items.Behaviors;

public sealed class MapBehavior : IItemBehavior
{
    private const short MapWidth = 128;
    private const short MapHeight = 128;

    public void InventoryTick(Item item, ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
        if (world.IsRemote)
        {
            return;
        }

        MapState mapState = GetMapState(itemStack.getDamage(), world);
        if (entity is EntityPlayer player)
        {
            mapState.Update(player, itemStack);
        }

        if (shouldUpdate)
        {
            Update(world, entity, mapState);
        }
    }

    public void OnCraft(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        itemStack.setDamage(world.StateManager.GetUniqueDataId("map"));
        string mapName = "map_" + itemStack.getDamage();
        MapState mapState = new(mapName);
        world.StateManager.SetData(mapName, mapState);
        mapState.CenterX = MathHelper.Floor(player.X);
        mapState.CenterZ = MathHelper.Floor(player.Z);
        mapState.Scale = 3;
        mapState.Dimension = (sbyte)world.Dimension.Id;
        mapState.MarkDirty();
    }

    public bool IsNetworkSynced(Item item) => true;

    public Packet? GetUpdatePacket(Item item, ItemStack stack, IWorldContext world, EntityPlayer player)
    {
        byte[]? updateData = GetMapState(stack.getDamage(), world).GetPlayerMarkerPacket(player);
        return updateData == null ? null : MapUpdateS2CPacket.Get((short)item.Id, (short)stack.getDamage(), updateData);
    }

    public static MapState GetMapState(int mapId, IWorldContext world)
    {
        string mapName = $"map_{mapId}";
        MapState? mapState = (MapState?)world.StateManager.LoadData(typeof(MapState), mapName);
        if (mapState != null)
        {
            return mapState;
        }

        mapState = new MapState(mapName);
        world.StateManager.SetData(mapName, mapState);
        return mapState;
    }

    public static MapState GetSavedMapState(ItemStack stack, IWorldContext world)
    {
        string mapName = "map_" + stack.getDamage();
        MapState? mapState = (MapState?)world.StateManager.LoadData(typeof(MapState), mapName);
        if (mapState != null)
        {
            return mapState;
        }

        stack.setDamage(world.StateManager.GetUniqueDataId("map"));
        mapState = new MapState(mapName);
        mapState.CenterX = world.Properties.SpawnX;
        mapState.CenterZ = world.Properties.SpawnZ;
        mapState.Scale = 3;
        mapState.Dimension = (sbyte)world.Dimension.Id;
        mapState.MarkDirty();
        world.StateManager.SetData(mapName, mapState);
        return mapState;
    }

    private static void Update(IWorldContext world, Entity entity, MapState map)
    {
        if (world.Dimension.Id != map.Dimension)
        {
            return;
        }

        int blocksPerPixel = 1 << map.Scale;
        int centerX = map.CenterX;
        int centerZ = map.CenterZ;
        int entityPosX = MathHelper.Floor(entity.X - centerX) / blocksPerPixel + MapWidth / 2;
        int entityPosZ = MathHelper.Floor(entity.Z - centerZ) / blocksPerPixel + MapHeight / 2;
        int scanRadius = 128 / blocksPerPixel;
        if (world.Dimension.HasCeiling)
        {
            scanRadius /= 2;
        }

        ++map.InventoryTicks;

        for (int pixelX = entityPosX - scanRadius + 1; pixelX < entityPosX + scanRadius; ++pixelX)
        {
            if ((pixelX & 15) != (map.InventoryTicks & 15))
            {
                continue;
            }

            int minDirtyZ = 255;
            int maxDirtyZ = 0;
            double lastHeight = 0.0D;

            for (int pixelZ = entityPosZ - scanRadius - 1; pixelZ < entityPosZ + scanRadius; ++pixelZ)
            {
                if (pixelX < 0 || pixelZ < -1 || pixelX >= MapWidth || pixelZ >= MapHeight)
                {
                    continue;
                }

                int dx = pixelX - entityPosX;
                int dy = pixelZ - entityPosZ;
                bool isOutside = dx * dx + dy * dy > (scanRadius - 2) * (scanRadius - 2);
                int worldX = (centerX / blocksPerPixel + pixelX - MapWidth / 2) * blocksPerPixel;
                int worldZ = (centerZ / blocksPerPixel + pixelZ - MapHeight / 2) * blocksPerPixel;
                int[] blockHistogram = new int[256];
                Chunk chunk = world.ChunkHost.GetChunkFromPos(worldX, worldZ);
                int chunkOffsetX = worldX & 15;
                int chunkOffsetZ = worldZ & 15;
                int fluidDepth = 0;
                double avgHeight = 0.0D;
                int sampleX, sampleZ, currentY, colorIndex;

                if (world.Dimension.HasCeiling)
                {
                    sampleX = worldX + worldZ * 231871;
                    sampleX = sampleX * sampleX * 31287121 + sampleX * 11;
                    blockHistogram[((sampleX >> 20) & 1) == 0 ? Block.Dirt.id : Block.Stone.id] += 10;
                    avgHeight = 100.0D;
                }
                else
                {
                    for (sampleX = 0; sampleX < blocksPerPixel; ++sampleX)
                    {
                        for (sampleZ = 0; sampleZ < blocksPerPixel; ++sampleZ)
                        {
                            currentY = chunk.GetHeight(sampleX + chunkOffsetX, sampleZ + chunkOffsetZ) + 1;
                            int blockId = 0;
                            if (currentY > 1)
                            {
                                ProcessBlockHeight(chunk, sampleX, chunkOffsetX, sampleZ, chunkOffsetZ, ref currentY, out blockId, ref fluidDepth);
                            }

                            avgHeight += currentY / (double)(blocksPerPixel * blocksPerPixel);
                            ++blockHistogram[blockId];
                        }
                    }
                }

                fluidDepth /= blocksPerPixel * blocksPerPixel;
                sampleX = 0;
                sampleZ = 0;
                for (currentY = 0; currentY < 256; ++currentY)
                {
                    if (blockHistogram[currentY] > sampleX)
                    {
                        sampleZ = currentY;
                        sampleX = blockHistogram[currentY];
                    }
                }

                double shadeFactor = (avgHeight - lastHeight) * 4.0D / (blocksPerPixel + 4) + (((pixelX + pixelZ) & 1) - 0.5D) * 0.4D;
                byte brightness = 1;
                if (shadeFactor > 0.6D)
                {
                    brightness = 2;
                }

                if (shadeFactor < -0.6D)
                {
                    brightness = 0;
                }

                colorIndex = 0;
                if (sampleZ > 0)
                {
                    MapColor mapColor = Block.Blocks[sampleZ].material.MapColor;
                    if (mapColor == MapColor.Water)
                    {
                        shadeFactor = fluidDepth * 0.1D + ((pixelX + pixelZ) & 1) * 0.2D;
                        brightness = 1;
                        if (shadeFactor < 0.5D)
                        {
                            brightness = 2;
                        }

                        if (shadeFactor > 0.9D)
                        {
                            brightness = 0;
                        }
                    }

                    colorIndex = mapColor.Id;
                }

                lastHeight = avgHeight;
                if (pixelZ >= 0 && dx * dx + dy * dy < scanRadius * scanRadius && (!isOutside || ((pixelX + pixelZ) & 1) != 0))
                {
                    byte currentColor = map.Colors[pixelX + pixelZ * MapWidth];
                    byte pixelColor = (byte)(colorIndex * 4 + brightness);
                    if (currentColor != pixelColor)
                    {
                        if (minDirtyZ > pixelZ)
                        {
                            minDirtyZ = pixelZ;
                        }

                        if (maxDirtyZ < pixelZ)
                        {
                            maxDirtyZ = pixelZ;
                        }

                        map.Colors[pixelX + pixelZ * MapWidth] = pixelColor;
                    }
                }
            }

            if (minDirtyZ <= maxDirtyZ)
            {
                map.MarkDirty(pixelX, minDirtyZ, maxDirtyZ);
            }
        }
    }

    private static void ProcessBlockHeight(Chunk chunk, int chunkX, int dx, int chunkZ, int dz, ref int scanY, out int blockId, ref int fluidDepth)
    {
        blockId = 0;
        bool exitLoop = false;

        while (!exitLoop)
        {
            bool foundSurface = true;
            blockId = chunk.GetBlockId(chunkX + dx, scanY - 1, chunkZ + dz);
            if (blockId == 0)
            {
                foundSurface = false;
            }
            else if (scanY > 0 && blockId > 0 && Block.Blocks[blockId].material.MapColor == MapColor.Air)
            {
                foundSurface = false;
            }

            if (!foundSurface)
            {
                --scanY;
                blockId = chunk.GetBlockId(chunkX + dx, scanY - 1, chunkZ + dz);
            }

            if (foundSurface)
            {
                if (blockId == 0 || !Block.Blocks[blockId].material.IsFluid)
                {
                    exitLoop = true;
                }
                else
                {
                    int depthCheckY = scanY - 1;
                    while (true)
                    {
                        int fluidBlockId = chunk.GetBlockId(chunkX + dx, depthCheckY--, chunkZ + dz);
                        ++fluidDepth;
                        if (depthCheckY <= 0 || fluidBlockId == 0 || !Block.Blocks[fluidBlockId].material.IsFluid)
                        {
                            exitLoop = true;
                            break;
                        }
                    }
                }
            }
        }
    }
}
