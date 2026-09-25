using System.IO.Compression;
using Microsoft.Extensions.Logging;
using OmniBlock.Blocks.Entities;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Worlds.Chunks.Storage;

internal class RegionChunkStorage : IChunkStorage
{
    private readonly string _dir;
    private readonly ILogger<RegionChunkStorage> _logger = Log.Instance.For<RegionChunkStorage>();

    public RegionChunkStorage(string inputDir) => _dir = inputDir;

    public bool ContainsChunk(int chunkX, int chunkZ) => RegionIo.ContainsChunk(_dir, chunkX, chunkZ);

    /// <summary>
    ///     Reads only the saved terrain arrays. Open gameplay regions are read through their
    ///     cached handle; unopened regions use a separate read-only handle. The ordinary RegionIo
    ///     path can create a file on a miss, while constructing a Chunk activates saved entities.
    /// </summary>
    public TerrainLodSourceSnapshot? ReadTerrainLodSource(
        int chunkX, int chunkZ, bool hasSkyLight)
    {
        if (RegionIo.TryGetCachedChunkInputStream(_dir, chunkX, chunkZ, out var cached))
        {
            using (cached)
                return cached is null ? null : ReadTerrainSource(
                    cached.Stream, chunkX, chunkZ, hasSkyLight);
        }
        var path = Path.Combine(_dir, "region",
            $"r.{chunkX >> 5}.{chunkZ >> 5}.mcr");
        if (!File.Exists(path)) return null;

        using FileStream file = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, bufferSize: 4096, FileOptions.SequentialScan);
        if (file.Length < 8192)
            throw new InvalidDataException($"Region file '{path}' has no complete header.");
        var slot = (chunkX & 31) + ((chunkZ & 31) << 5);
        file.Position = slot * sizeof(int);
        var offset = file.ReadInt();
        if (offset == 0) return null;
        var sector = offset >> 8;
        var sectors = offset & 255;
        if (sector < 2 || sectors == 0 ||
            (long)(sector + sectors) * 4096 > file.Length)
            throw new InvalidDataException(
                $"Saved chunk {chunkX},{chunkZ} has an invalid region offset.");

        file.Position = (long)sector * 4096;
        var compressedLength = file.ReadInt();
        if (compressedLength <= 1 || compressedLength > sectors * 4096 - sizeof(int))
            throw new InvalidDataException(
                $"Saved chunk {chunkX},{chunkZ} has an invalid compressed length.");
        if (file.ReadByte() != (byte)RegionFile.CompressionType.ZLibDeflate)
            throw new InvalidDataException(
                $"Saved chunk {chunkX},{chunkZ} has an unsupported compression type.");
        var compressed = new byte[compressedLength - 1];
        file.ReadExactly(compressed);
        using MemoryStream payload = new(compressed, writable: false);
        using ZLibStream decompressed = new(payload, CompressionMode.Decompress);
        return ReadTerrainSource(decompressed, chunkX, chunkZ, hasSkyLight);
    }

    private static TerrainLodSourceSnapshot ReadTerrainSource(
        Stream decompressed, int chunkX, int chunkZ, bool hasSkyLight)
    {
        var root = NbtIo.Read(decompressed);
        if (!root.HasKey("Level"))
            throw new InvalidDataException($"Saved chunk {chunkX},{chunkZ} has no Level compound.");
        var level = root.GetCompoundTag("Level");
        if (!HasExpectedCoordinates(level, chunkX, chunkZ))
            throw new InvalidDataException(
                $"Saved chunk slot {chunkX},{chunkZ} contains terrain for " +
                $"{level.GetInteger("xPos")},{level.GetInteger("zPos")}.");
        return TerrainLodSourceSnapshot.FromRegionNbt(level, hasSkyLight: hasSkyLight);
    }

    public Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ)
    {
        using var s = RegionIo.GetChunkInputStream(_dir, chunkX, chunkZ);
        if (s == null)
        {
            return null;
        }

        var stream = s.Stream;

        if (stream != null)
        {
            NBTTagCompound chunkTag;
            try
            {
                chunkTag = NbtIo.Read(stream);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
            {
                _logger.LogWarning(
                    exception,
                    "Ignoring unreadable chunk file at {ChunkX},{ChunkZ}; the requested chunk will be regenerated.",
                    chunkX,
                    chunkZ);
                return null;
            }

            if (!chunkTag.HasKey("Level"))
            {
                _logger.LogInformation($"Chunk file at {chunkX},{chunkZ} is missing level data, skipping");
                return null;
            }

            if (!chunkTag.GetCompoundTag("Level").HasKey("Blocks"))
            {
                _logger.LogInformation($"Chunk file at {chunkX},{chunkZ} is missing block data, skipping");
                return null;
            }

            var levelTag = chunkTag.GetCompoundTag("Level");
            var storedX = levelTag.GetInteger("xPos");
            var storedZ = levelTag.GetInteger("zPos");
            if (!HasExpectedCoordinates(levelTag, chunkX, chunkZ))
            {
                // The payload can contain entities and block entities whose world coordinates
                // still belong to the embedded chunk. Relabelling only xPos/zPos creates a mixed
                // chunk and causes those entities to be reinserted forever. Treat this slot as
                // missing so the generator can replace it safely.
                _logger.LogWarning(
                    "Ignoring chunk file at {ExpectedX},{ExpectedZ} because its payload belongs " +
                    "to {ActualX},{ActualZ}; the requested chunk will be regenerated.",
                    chunkX,
                    chunkZ,
                    storedX,
                    storedZ);
                return null;
            }

            var chunk = LoadChunkFromNbt(world, levelTag);

            chunk.Fill();
            return chunk;
        }

        return null;
    }

    public ChunkSaveResult SaveChunk(IWorldContext world, Chunk chunk, Action? onSave, long sequence)
    {
        var stream = RegionIo.GetChunkOutputStream(_dir, chunk.X, chunk.Z);
        if (stream == null)
        {
            throw new IOException($"Could not open region output for chunk {chunk.X},{chunk.Z}.");
        }

        using (stream)
        {
            NBTTagCompound tag = new();
            NBTTagCompound levelTag = new();
            tag.SetTag("Level", levelTag);
            storeChunkInCompound(chunk, world, levelTag);
            NbtIo.Write(tag, stream);
        }

        var sizeDelta = RegionIo.GetSizeDelta(_dir, chunk.X, chunk.Z);
        world.Properties.SizeOnDisk += sizeDelta;
        onSave?.Invoke();
        return new ChunkSaveResult(Math.Max(0, sizeDelta));
    }

    public void SaveEntities(IWorldContext world, Chunk chunk)
    {
    }

    public void Tick()
    {
    }

    public void Flush()
    {
        RegionIo.FlushWorld(_dir, flushToDisk: false);
    }

    public void FlushToDisk()
    {
        RegionIo.FlushWorld(_dir, flushToDisk: true);
    }

    internal static bool HasExpectedCoordinates(NBTTagCompound levelTag, int chunkX, int chunkZ) =>
        levelTag.GetInteger("xPos") == chunkX && levelTag.GetInteger("zPos") == chunkZ;

    public static void storeChunkInCompound(Chunk chunk, IWorldContext world, NBTTagCompound nbt)
    {
        nbt.SetInteger("xPos", chunk.X);
        nbt.SetInteger("zPos", chunk.Z);
        nbt.SetLong("LastUpdate", world.GetTime());
        nbt.SetLong("TerrainRevision", chunk.TerrainRevision);
        nbt.SetByteArray("Blocks", chunk.Blocks);
        nbt.SetByteArray("Data", chunk.Meta.Bytes);
        nbt.SetByteArray("SkyLight", chunk.SkyLight.Bytes);
        nbt.SetByteArray("BlockLight", chunk.BlockLight.Bytes);
        nbt.SetByteArray("HeightMap", chunk.HeightMap);
        nbt.SetBoolean("TerrainPopulated", chunk.TerrainPopulated);
        chunk.LastSaveHadEntities = false;
        NBTTagList entityTags = new();

        NBTTagCompound entityTag;
        for (var entitySlice = 0; entitySlice < chunk.Entities.Length; ++entitySlice)
        {
            foreach (var entity in chunk.Entities[entitySlice])
            {
                chunk.LastSaveHadEntities = true;
                entityTag = new NBTTagCompound();
                if (entity.SaveSelfNbt(entityTag))
                {
                    entityTags.SetTag(entityTag);
                }
            }
        }

        nbt.SetTag("Entities", entityTags);
        NBTTagList blockEntityTags = new();

        foreach (var blockEntity in chunk.BlockEntities.Values)
        {
            entityTag = new NBTTagCompound();
            blockEntity.WriteNbt(entityTag);
            blockEntityTags.SetTag(entityTag);
        }

        nbt.SetTag("TileEntities", blockEntityTags);

        if (world.IsRemote) return;

        NBTTagList tileTickTags = new();
        var worldTime = world.GetTime();
        foreach (var (x, y, z, blockId, scheduledTime, scheduledOrder) in world.TickScheduler.GetPendingTicksInChunk(chunk.X, chunk.Z))
        {
            var delta = scheduledTime - worldTime;
            var t = (int)Math.Clamp(delta, int.MinValue, int.MaxValue);
            var p = scheduledOrder > int.MaxValue ? int.MaxValue : (int)scheduledOrder;
            NBTTagCompound tickTag = new();
            tickTag.SetInteger("x", x);
            tickTag.SetInteger("y", y);
            tickTag.SetInteger("z", z);
            tickTag.SetInteger("t", t);
            tickTag.SetInteger("p", p);
            tickTag.SetInteger("i", blockId);
            tileTickTags.SetTag(tickTag);
        }

        foreach (var (x, y, z, blockId, delay) in chunk.GetPendingActivationTicks())
        {
            NBTTagCompound tickTag = new();
            tickTag.SetInteger("x", x);
            tickTag.SetInteger("y", y);
            tickTag.SetInteger("z", z);
            tickTag.SetInteger("t", delay);
            tickTag.SetInteger("p", 0);
            tickTag.SetInteger("i", blockId);
            tileTickTags.SetTag(tickTag);
        }

        if (tileTickTags.TagCount() > 0)
        {
            nbt.SetTag("TileTicks", tileTickTags);
        }
    }

    public static Chunk LoadChunkFromNbt(IWorldContext world, NBTTagCompound nbt)
    {
        Chunk chunk = new(world, nbt);
        var chunkX = chunk.X;
        var chunkZ = chunk.Z;

        if (!chunk.Meta.IsInitialized)
        {
            chunk.Meta = new ChunkNibbleArray(chunk.Blocks.Length);
        }

        if (chunk.HeightMap == null || !chunk.SkyLight.IsInitialized)
        {
            chunk.SkyLight = new ChunkNibbleArray(chunk.Blocks.Length);
            chunk.PopulateHeightMap();
        }
        else if (chunk.HeightMap.Length == Chunk.DefaultHeightMapHeight)
        {
            foreach (var height in chunk.HeightMap)
            {
                if (height >= ChuckFormat.WorldHeight)
                {
                    chunk.PopulateHeightMapOnly();
                    break;
                }
            }
        }

        if (!chunk.BlockLight.IsInitialized)
        {
            chunk.BlockLight = new ChunkNibbleArray(chunk.Blocks.Length);
            chunk.PopulateLight();
        }

        var entityTags = nbt.GetTagList("Entities");
        if (entityTags != null)
        {
            for (var entityIndex = 0; entityIndex < entityTags.TagCount(); ++entityIndex)
            {
                var entityTag = (NBTTagCompound)entityTags.TagAt(entityIndex);
                var entity = world.Content.EntityTypes.ReadFromNbt(entityTag, world);
                chunk.LastSaveHadEntities = true;
                if (entity != null)
                {
                    chunk.AddEntity(entity);
                }
            }
        }

        var blockEntityTags = nbt.GetTagList("TileEntities");
        if (blockEntityTags != null)
        {
            for (var blockEntityIndex = 0; blockEntityIndex < blockEntityTags.TagCount(); ++blockEntityIndex)
            {
                var blockEntityTag = (NBTTagCompound)blockEntityTags.TagAt(blockEntityIndex);
                var blockEntity = BlockEntity.CreateFromNbt(world, blockEntityTag);
                if (blockEntity != null)
                {
                    chunk.AddBlockEntity(blockEntity);
                }
            }
        }

        if (world.IsRemote || !nbt.HasKey("TileTicks")) return chunk;

        var tileTickTags = nbt.GetTagList("TileTicks");
        var minWx = chunkX * 16;
        var maxWx = minWx + 15;
        var minWz = chunkZ * 16;
        var maxWz = minWz + 15;

        for (var i = 0; i < tileTickTags.TagCount(); i++)
        {
            try
            {
                if (tileTickTags.TagAt(i) is not NBTTagCompound tickTag)
                {
                    continue;
                }

                if (!tickTag.HasKey("i"))
                {
                    continue;
                }

                var blockId = tickTag.GetInteger("i");
                if (blockId <= 0 || !world.Content.Blocks.TryGetByProtocolId(blockId, out _))
                {
                    continue;
                }

                var x = tickTag.GetInteger("x");
                var y = tickTag.GetInteger("y");
                var z = tickTag.GetInteger("z");
                if (y < 0 || y >= ChuckFormat.WorldHeight || x < minWx || x > maxWx || z < minWz || z > maxWz)
                {
                    Log.Instance.For<RegionChunkStorage>().LogDebug("Skipping TileTicks entry with out-of-range coordinates ({X},{Y},{Z}) for chunk {ChunkX},{ChunkZ}", x, y, z, chunkX, chunkZ);
                    continue;
                }

                var t = tickTag.GetInteger("t");
                chunk.QueueActivationTick(x, y, z, blockId, t);
            }
            catch (InvalidCastException)
            {
                Log.Instance.For<RegionChunkStorage>().LogDebug("Skipping TileTicks entry with unexpected NBT types at index {Index}", i);
            }
        }


        return chunk;
    }
}
