using Microsoft.Extensions.Logging;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Network.Chunks;
using OmniBlock.Profiling;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Chunks;

public class Chunk
{
    // (15 << 4) + 15 == 256
    internal const int DefaultHeightMapHeight = 256;

    /// <summary>The height of one light section, and how many of them a column is divided into.</summary>
    /// <remarks>
    ///     Light travels as whole sections rather than as one cell at a time, so that what goes out
    ///     is a consistent snapshot of the array rather than a value read while propagation was
    ///     still running. Sixteen matches the cube the client meshes, so a section arriving dirties
    ///     exactly one mesh.
    /// </remarks>
    public const int LightSectionHeight = 16;

    /// <summary>Bytes one section occupies in one light array: 256 columns of 16 nibbles.</summary>
    public const int LightSectionBytes = 16 * 16 * LightSectionHeight / 2;

    /// <summary>What <see cref="CopyLightSection" /> writes and <see cref="ApplyLightSection" /> reads.</summary>
    public const int LightSectionPayloadBytes = LightSectionBytes * 2;

    /// <summary>
    ///     A section is not contiguous. The index is <c>(x &lt;&lt; 11) | (z &lt;&lt; 7) | y</c>, so
    ///     y is the fastest axis and a slice of the column is 256 runs of eight bytes, one per
    ///     column, spaced 64 apart.
    /// </summary>
    private const int ColumnBytes = ChunkHeightForStride / 2;

    private const int ChunkHeightForStride = 128;
    public static bool HasSkyLight;

    private static readonly ILogger<Chunk> s_logger = Log.Instance.For<Chunk>();

    // The value in a hightmap is HeightMap[chunkZ << 4 | chunk] == height
    public readonly byte[] HeightMap = new byte[DefaultHeightMapHeight];
    public readonly int X;
    public readonly int Z;

    public Dictionary<BlockPos, BlockEntity> BlockEntities;
    public ChunkNibbleArray BlockLight;

    public byte[] Blocks;
    public bool Dirty;
    public List<Entity>[] Entities;
    public bool LastSaveHadEntities;
    public long LastSaveTime;

    public bool Loaded;
    public ChunkNibbleArray Meta;
    public int MinHeightMapValue;
    public ChunkNibbleArray SkyLight;
    public bool TerrainPopulated;
    public IWorldContext World;

    public Chunk(IWorldContext world, int x, int z)
    {
        BlockEntities = [];
        Entities = new List<Entity>[8];
        TerrainPopulated = false;
        Dirty = false;
        LastSaveHadEntities = false;
        LastSaveTime = 0L;
        World = world;
        X = x;
        Z = z;

        for (var i = 0; i < Entities.Length; i++)
        {
            Entities[i] = [];
        }
    }

    public Chunk(IWorldContext world, NBTTagCompound nbt) : this(world, nbt.GetInteger("xPos"), nbt.GetInteger("zPos"))
    {
        Blocks = nbt.GetByteArray("Blocks");
        HeightMap = nbt.GetByteArray("HeightMap");
        TerrainPopulated = nbt.GetBoolean("TerrainPopulated");

        Meta = new ChunkNibbleArray(nbt.GetByteArray("Data"));
        SkyLight = new ChunkNibbleArray(nbt.GetByteArray("SkyLight"));
        BlockLight = new ChunkNibbleArray(nbt.GetByteArray("BlockLight"));
    }

    public Chunk(IWorldContext world, byte[] blocks, int x, int z) : this(world, x, z)
    {
        Blocks = blocks;
        Meta = new ChunkNibbleArray(blocks.Length);
        SkyLight = new ChunkNibbleArray(blocks.Length);
        BlockLight = new ChunkNibbleArray(blocks.Length);
    }

    /// <inheritdoc cref="LightSectionHeight" />
    public static int LightSectionCount => ChuckFormat.ChunkHeight / LightSectionHeight;

    /// <summary>
    ///     Which sections have had light written into them since anything last took them.
    /// </summary>
    /// <remarks>
    ///     Held here, beside the arrays, rather than on <c>LightingEngine</c>. Its
    ///     <c>SetLight</c> is not the only writer and never was: <see cref="PopulateHeightMap" />
    ///     fills the sky array directly, and a mask driven from the engine would be blind to
    ///     exactly the pass that establishes a chunk's light in the first place. Every writer goes
    ///     through this type, so tracking it here is the only placement where being complete is
    ///     structural rather than a thing to remember.
    /// </remarks>
    public uint LightDirtySections { get; private set; }

    public virtual int this[int x, int y, int z]
    {
        get => Blocks[ChuckFormat.GetIndex(x, y, z)];
        set => Blocks[ChuckFormat.GetIndex(x, y, z)] = (byte)value;
    }

    public virtual bool ChunkPosEquals(int x, int z) => x == X && z == Z;

    public virtual int GetHeight(int localX, int localZ) => HeightMap[(localZ << 4) | localX];

    /// <summary>
    ///     Lights a chunk whose block light was never stored, by the same route as
    ///     <see cref="PopulateBlockLight" />.
    /// </summary>
    public virtual void PopulateLight() => PopulateBlockLight();

    public virtual void PopulateHeightMapOnly()
    {
        var h = ChuckFormat.ChunkHeight - 1;
        var minHeight = h;

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var y = h;
                var index = ChuckFormat.GetIndex(localX, localZ);

                while (y > 0 && World.Content.Blocks.GetOpacity(Blocks[index + y - 1]) == 0)
                {
                    --y;
                }

                HeightMap[(localZ << 4) | localX] = (byte)y;
                if (y < minHeight) minHeight = y;
            }
        }

        MinHeightMapValue = minHeight;
        Dirty = true;
    }

    public virtual void PopulateHeightMap()
    {
        var h = ChuckFormat.ChunkHeight - 1;
        var minHeight = h;

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var y = h;
                var index = ChuckFormat.GetIndex(localX, localZ);

                while (y > 0 && World.Content.Blocks.GetOpacity(Blocks[index + y - 1]) == 0)
                {
                    --y;
                }

                HeightMap[(localZ << 4) | localX] = (byte)y;
                if (y < minHeight) minHeight = y;

                if (!World.Dimension.HasCeiling)
                {
                    var lightLevel = 15;
                    var currentY = h;

                    do
                    {
                        lightLevel -= World.Content.Blocks.GetOpacity(Blocks[index + currentY]);
                        if (lightLevel > 0)
                        {
                            SkyLight.SetNibble(localX, currentY, localZ, lightLevel);
                        }

                        --currentY;
                    } while (currentY > 0 && lightLevel > 0);
                }
            }
        }

        MinHeightMapValue = minHeight;

        for (var localX = 0; localX < 16; ++localX)
        {
            for (var localZ = 0; localZ < 16; ++localZ)
            {
                LightGaps(localX, localZ);
            }
        }

        // The fill above wrote the sky array directly, so nothing that watches LightingEngine saw
        // any of it. Without this the pass that establishes a chunk's light is the one pass no
        // reader can be told about, and a client sent the chunk beforehand keeps zeros for good.
        MarkAllLightDirty();
        Dirty = true;
    }

    /// <summary>
    ///     Queues a block-light update for every light source already sitting in this chunk's
    ///     terrain.
    /// </summary>
    /// <remarks>
    ///     Block light only ever spreads from an update, and the only thing that queues one is a
    ///     block being placed through <see cref="SetBlock" />. Terrain that arrives with its light
    ///     sources already in place — every lava pool a generator carved, every chunk read back
    ///     without a stored BlockLight array — goes through no such call, so without this pass it
    ///     stays at zero until something happens to touch it.
    /// </remarks>
    public virtual void PopulateBlockLight()
    {
        for (var localX = 0; localX < 16; ++localX)
        {
            var worldX = X * 16 + localX;

            for (var localZ = 0; localZ < 16; ++localZ)
            {
                var worldZ = Z * 16 + localZ;
                var column = ChuckFormat.GetIndex(localX, localZ);

                for (var y = 0; y < ChuckFormat.ChunkHeight; ++y)
                {
                    if (World.Content.Blocks.GetLightEmission(Blocks[column + y]) == 0)
                    {
                        continue;
                    }

                    World.Lighting.QueueLightUpdate(LightType.Block, worldX, y, worldZ, worldX, y, worldZ);
                }
            }
        }
    }

    private void LightGaps(int localX, int localZ)
    {
        var height = GetHeight(localX, localZ);
        var worldX = X * 16 + localX;
        var worldZ = Z * 16 + localZ;

        LightGap(worldX - 1, worldZ, height);
        LightGap(worldX + 1, worldZ, height);
        LightGap(worldX, worldZ - 1, height);
        LightGap(worldX, worldZ + 1, height);
    }

    private void LightGap(int worldX, int worldZ, int height)
    {
        var topY = World.Reader.GetTopY(worldX, worldZ);
        if (topY > height)
        {
            World.Lighting.QueueLightUpdate(LightType.Sky, worldX, height, worldZ, worldX, topY, worldZ);
            Dirty = true;
        }
        else if (topY < height)
        {
            World.Lighting.QueueLightUpdate(LightType.Sky, worldX, topY, worldZ, worldX, height, worldZ);
            Dirty = true;
        }
    }

    private void UpdateHeightMap(int localX, int y, int localZ)
    {
        int oldHeight = HeightMap[(localZ << 4) | localX];
        var newHeight = oldHeight;

        if (y > oldHeight) newHeight = y;

        var index = ChuckFormat.GetIndex(localX, localZ);
        while (newHeight > 0 && World.Content.Blocks.GetOpacity(Blocks[index + newHeight - 1]) == 0)
        {
            --newHeight;
        }

        if (newHeight == oldHeight) return;

        World.Broadcaster.SetBlocksDirty(localX, localZ, newHeight, oldHeight);
        HeightMap[(localZ << 4) | localX] = (byte)newHeight;

        if (newHeight < MinHeightMapValue)
        {
            MinHeightMapValue = newHeight;
        }
        else
        {
            var min = ChuckFormat.ChunkHeight - 1;
            for (var i = 0; i < 16; ++i)
            {
                for (var j = 0; j < 16; ++j)
                {
                    if (HeightMap[(j << 4) | i] < min)
                    {
                        min = HeightMap[(j << 4) | i];
                    }
                }
            }

            MinHeightMapValue = min;
        }

        var worldX = X * 16 + localX;
        var worldZ = Z * 16 + localZ;

        // The heightmap itself is still recomputed on a remote world above — the renderer needs
        // it and the wire carries none. The sky light that follows from a height change is not:
        // a remote world holds the sky the wire wrote, and the section snapshot that follows this
        // change overwrites anything computed here. Writing it locally would just race that
        // snapshot.
        if (!World.IsRemote)
        {
            // Both branches write the sky array directly, as the first fill does, so both have to
            // say so themselves.
            MarkLightDirty(Math.Min(oldHeight, newHeight), Math.Max(oldHeight, newHeight));

            if (newHeight < oldHeight)
            {
                for (var currY = newHeight; currY < oldHeight; ++currY)
                {
                    SkyLight.SetNibble(localX, currY, localZ, 15);
                }
            }
            else
            {
                World.Lighting.QueueLightUpdate(LightType.Sky, worldX, oldHeight, worldZ, worldX, newHeight, worldZ);
                for (var currY = oldHeight; currY < newHeight; ++currY)
                {
                    SkyLight.SetNibble(localX, currY, localZ, 0);
                }
            }

            var lightLevel = 15;
            var updateY = newHeight;

            MarkLightDirty(0, newHeight);

            while (newHeight > 0 && lightLevel > 0)
            {
                SkyLight.SetNibble(localX, newHeight, localZ, lightLevel);
                --newHeight;

                var opacity = World.Content.Blocks.GetOpacity(GetBlockId(localX, newHeight, localZ));
                if (opacity == 0) opacity = 1;

                lightLevel -= opacity;
                if (lightLevel < 0) lightLevel = 0;
            }

            while (newHeight > 0 && World.Content.Blocks.GetOpacity(GetBlockId(localX, newHeight - 1, localZ)) == 0)
            {
                --newHeight;
            }

            if (newHeight != updateY)
            {
                World.Lighting.QueueLightUpdate(LightType.Sky, worldX - 1, newHeight, worldZ - 1, worldX + 1, updateY, worldZ + 1);
            }
        }

        Dirty = true;
    }

    public virtual int GetBlockId(int x, int y, int z) => this[x, y, z];


    public virtual bool SetBlock(int localX, int y, int localZ, int rawId, int meta, bool notifyBlockPlaced = true)
    {
        var pos = ChuckFormat.GetIndex(localX, y, localZ);
        var newId = (byte)rawId;
        int height = HeightMap[(localZ << 4) | localX];
        int oldId = Blocks[pos];

        var sameId = oldId == rawId;
        if (sameId && Meta.GetNibble(localX, y, localZ) == meta) return false;

        var worldX = X * 16 + localX;
        var worldZ = Z * 16 + localZ;
        Blocks[pos] = newId;

        if (notifyBlockPlaced && oldId != 0 && !World.IsRemote)
        {
            World.Content.Blocks.GetByProtocolId(oldId).OnBreak(new OnBreakEvent(World, null, worldX, y, worldZ));
        }

        Meta.SetNibble(localX, y, localZ, meta);

        if (!World.Dimension.HasCeiling)
        {
            if (World.Content.Blocks.GetOpacity(newId) != 0)
            {
                if (y >= height) UpdateHeightMap(localX, y + 1, localZ);
            }
            else if (y == height - 1)
            {
                UpdateHeightMap(localX, y, localZ);
            }

            World.Lighting.QueueLightUpdate(LightType.Sky, worldX, y, worldZ, worldX, y, worldZ);
        }

        World.Lighting.QueueLightUpdate(LightType.Block, worldX, y, worldZ, worldX, y, worldZ);
        LightGaps(localX, localZ);

        if (notifyBlockPlaced)
        {
            if (rawId != 0 && !World.IsRemote)
            {
                World.Content.Blocks.GetByProtocolId(rawId).OnPlaced(new OnPlacedEvent(World, null, 0, 0, worldX, y, worldZ));
            }

            if (sameId)
            {
                World.Content.Blocks.GetByProtocolId(rawId).OnMetadataChange(new OnMetadataChangeEvent(World, worldX, y, worldZ, meta));
            }
        }

        Dirty = true;
        return true;
    }

    public virtual bool SetBlock(int localX, int y, int localZ, int rawId, bool notifyBlockPlaced = true)
    {
        var pos = ChuckFormat.GetIndex(localX, y, localZ);
        var newId = (byte)rawId;
        int height = HeightMap[(localZ << 4) | localX];
        int oldId = Blocks[pos];

        if (oldId == rawId) return false;

        var worldX = X * 16 + localX;
        var worldZ = Z * 16 + localZ;
        Blocks[pos] = newId;

        if (oldId != 0)
        {
            World.Content.Blocks.GetByProtocolId(oldId).OnBreak(new OnBreakEvent(World, null, worldX, y, worldZ));
        }

        Meta.SetNibble(localX, y, localZ, 0);

        if (World.Content.Blocks.GetOpacity(newId) != 0)
        {
            if (y >= height) UpdateHeightMap(localX, y + 1, localZ);
        }
        else if (y == height - 1)
        {
            UpdateHeightMap(localX, y, localZ);
        }

        World.Lighting.QueueLightUpdate(LightType.Sky, worldX, y, worldZ, worldX, y, worldZ);
        World.Lighting.QueueLightUpdate(LightType.Block, worldX, y, worldZ, worldX, y, worldZ);
        LightGaps(localX, localZ);

        if (notifyBlockPlaced && rawId != 0 && !World.IsRemote)
        {
            World.Content.Blocks.GetByProtocolId(rawId).OnPlaced(new OnPlacedEvent(World, null, 0, 0, worldX, y, worldZ));
        }

        Dirty = true;
        return true;
    }

    public virtual int GetBlockMeta(int x, int y, int z) => Meta.GetNibble(x, y, z);

    public virtual void SetBlockMeta(int x, int y, int z, int meta)
    {
        Dirty = true;
        Meta.SetNibble(x, y, z, meta);
    }

    public virtual int GetLight(LightType lightType, int x, int y, int z) => lightType == LightType.Sky ? SkyLight.GetNibble(x, y, z) : lightType == LightType.Block ? BlockLight.GetNibble(x, y, z) : 0;

    public virtual void SetLight(LightType lightType, int x, int y, int z, int value)
    {
        Dirty = true;
        MarkLightDirty(y);
        if (lightType == LightType.Sky) SkyLight.SetNibble(x, y, z, value);
        else if (lightType == LightType.Block) BlockLight.SetNibble(x, y, z, value);
    }

    /// <summary>Records that the section holding <paramref name="y" /> has had light written.</summary>
    public void MarkLightDirty(int y)
    {
        if ((uint)y < (uint)ChuckFormat.ChunkHeight)
        {
            LightDirtySections |= 1u << (y / LightSectionHeight);
        }
    }

    /// <summary>Records the sections spanned by a range, inclusive, clamped to the column.</summary>
    public void MarkLightDirty(int minY, int maxY)
    {
        for (var section = Math.Max(minY, 0) / LightSectionHeight;
             section <= Math.Min(maxY, ChuckFormat.ChunkHeight - 1) / LightSectionHeight;
             section++)
        {
            LightDirtySections |= 1u << section;
        }
    }

    /// <summary>Records that every section has had light written.</summary>
    /// <remarks>
    ///     For the passes that fill the whole column at once. Naming each section they touched
    ///     would be exact and would cost more than sending them: the passes run when a chunk has
    ///     just arrived, which is when nearly every section is dirty anyway.
    /// </remarks>
    public void MarkAllLightDirty() =>
        LightDirtySections = LightSectionCount >= 32 ? uint.MaxValue : (1u << LightSectionCount) - 1u;

    /// <summary>Reads the dirty sections and clears them, so each change is claimed once.</summary>
    public uint TakeLightDirtySections()
    {
        var taken = LightDirtySections;
        LightDirtySections = 0;
        return taken;
    }

    /// <summary>The sky and block nibbles for one section, sky first.</summary>
    /// <remarks>
    ///     A whole section rather than the cells that changed: what makes this worth sending at all
    ///     is that the receiver ends up holding a copy of the array rather than a sequence of edits
    ///     it has to have applied in order and in full.
    /// </remarks>
    public void CopyLightSection(int section, Span<byte> destination)
    {
        const int runBytes = LightSectionHeight / 2;
        var start = section * runBytes;

        for (var column = 0; column < 256; column++)
        {
            var source = column * ColumnBytes + start;
            var target = column * runBytes;

            SkyLight.Bytes.AsSpan(source, runBytes).CopyTo(destination[target..]);
            BlockLight.Bytes.AsSpan(source, runBytes).CopyTo(destination[(LightSectionBytes + target)..]);
        }
    }

    /// <summary>Overwrites one section's light with a copy taken elsewhere.</summary>
    public void ApplyLightSection(int section, ReadOnlySpan<byte> source)
    {
        const int runBytes = LightSectionHeight / 2;
        var start = section * runBytes;

        for (var column = 0; column < 256; column++)
        {
            var target = column * ColumnBytes + start;
            var origin = column * runBytes;

            source.Slice(origin, runBytes).CopyTo(SkyLight.Bytes.AsSpan(target, runBytes));
            source.Slice(LightSectionBytes + origin, runBytes).CopyTo(BlockLight.Bytes.AsSpan(target, runBytes));
        }

        Dirty = true;
    }

    /// <summary>
    ///     Both light values for a cell in one byte: block light in the low nibble, sky light in the
    ///     high one. This is the form the block-update messages carry.
    /// </summary>
    public byte GetPackedLight(int x, int y, int z) =>
        (byte)(BlockLight.GetNibble(x, y, z) | (SkyLight.GetNibble(x, y, z) << 4));

    /// <summary>
    ///     Writes both light values from the packed form. Returns whether either changed, so a
    ///     caller can skip rebuilding the mesh when an update repeats what the chunk already held.
    /// </summary>
    public bool SetPackedLight(int x, int y, int z, byte packed)
    {
        var block = packed & 0xF;
        var sky = (packed >> 4) & 0xF;

        if (BlockLight.GetNibble(x, y, z) == block && SkyLight.GetNibble(x, y, z) == sky)
        {
            return false;
        }

        BlockLight.SetNibble(x, y, z, block);
        SkyLight.SetNibble(x, y, z, sky);
        MarkLightDirty(y);
        Dirty = true;
        return true;
    }

    public virtual int GetLight(int x, int y, int z, int ambientDarkness)
    {
        var sky = SkyLight.GetNibble(x, y, z);
        if (sky > 0) HasSkyLight = true;

        sky -= ambientDarkness;
        var block = BlockLight.GetNibble(x, y, z);

        return block > sky ? block : sky;
    }

    public virtual void AddEntity(Entity entity)
    {
        LastSaveHadEntities = true;
        var chunkX = MathHelper.Floor(entity.X / 16.0D);
        var chunkZ = MathHelper.Floor(entity.Z / 16.0D);

        if (chunkX != X || chunkZ != Z)
        {
            s_logger.LogWarning($"Entity in wrong chunk location! {entity}");
            s_logger.LogDebug(Environment.StackTrace);
        }

        var slice = MathHelper.Floor(entity.Y / 16.0D);
        if (slice < 0) slice = 0;
        if (slice >= Entities.Length) slice = Entities.Length - 1;

        entity.IsPersistent = true;
        entity.ChunkX = X;
        entity.ChunkSlice = slice;
        entity.ChunkZ = Z;
        Entities[slice].Add(entity);
    }

    public virtual void RemoveEntity(Entity entity) => RemoveEntity(entity, entity.ChunkSlice);

    public virtual void RemoveEntity(Entity entity, int chunkSlice)
    {
        if (chunkSlice < 0) chunkSlice = 0;
        if (chunkSlice >= Entities.Length) chunkSlice = Entities.Length - 1;

        Entities[chunkSlice].Remove(entity);
    }

    public virtual bool IsAboveMaxHeight(int localX, int y, int localZ) => y >= HeightMap[(localZ << 4) | localX];

    public virtual BlockEntity? GetBlockEntity(int localX, int y, int localZ)
    {
        BlockPos pos = new(localX, y, localZ);

        if (BlockEntities.TryGetValue(pos, out var entity))
        {
            if (entity != null && !entity.IsRemoved())
            {
                return entity;
            }
        }

        var worldX = X * 16 + localX;
        var worldZ = Z * 16 + localZ;

        entity = World.Entities.GetOrCreateBlockEntity<BlockEntity>(worldX, y, worldZ);

        if (entity != null)
        {
            BlockEntities[pos] = entity;
        }

        return entity;
    }

    /// <summary>Looks up an already-stored block entity without lazily manufacturing one via <see cref="Block.GetBlockEntity" />.</summary>
    public virtual BlockEntity? PeekBlockEntity(int localX, int y, int localZ)
    {
        BlockPos pos = new(localX, y, localZ);
        return BlockEntities.TryGetValue(pos, out var entity) && entity != null && !entity.IsRemoved() ? entity : null;
    }

    public virtual void AddBlockEntity(BlockEntity blockEntity)
    {
        var localX = blockEntity.X - X * 16;
        var localZ = blockEntity.Z - Z * 16;
        SetBlockEntity(localX, blockEntity.Y, localZ, blockEntity);

        if (Loaded) World.Entities.BlockEntities.Add(blockEntity);
    }

    public virtual void SetBlockEntity(int localX, int y, int localZ, BlockEntity blockEntity)
    {
        BlockPos pos = new(localX, y, localZ);
        blockEntity.World = World;
        blockEntity.X = X * 16 + localX;
        blockEntity.Y = y;
        blockEntity.Z = Z * 16 + localZ;

        var id = GetBlockId(localX, y, localZ);
        if (id != 0 && World.Content.Blocks.TryGetByProtocolId(id, out var block) && block.HasBlockEntity)
        {
            blockEntity.CancelRemoval();
            BlockEntities[pos] = blockEntity;
        }
        else
        {
            s_logger.LogWarning("Attempted to place a tile entity where there was no entity tile block!");
        }
    }

    public virtual void RemoveBlockEntityAt(int localX, int y, int localZ)
    {
        BlockPos pos = new(localX, y, localZ);
        if (Loaded && BlockEntities.Remove(pos, out var entity))
        {
            entity.MarkRemoved();
        }
    }

    public virtual void Load()
    {
        if (Loaded) return;

        Loaded = true;
        World.Entities.ProcessBlockUpdates(BlockEntities.Values);

        foreach (var list in Entities)
        {
            World.Entities.AddEntities(list);
        }
    }

    public virtual void Unload()
    {
        Loaded = false;

        foreach (var blockEntity in BlockEntities.Values)
        {
            blockEntity.MarkRemoved();
        }

        for (var sectionIndex = 0; sectionIndex < Entities.Length; ++sectionIndex)
        {
            World.Entities.UnloadEntities(Entities[sectionIndex]);
        }
    }

    public virtual void MarkDirty() => Dirty = true;

    public virtual void CollectOtherEntities(Entity except, Box box, List<Entity> result)
    {
        var minSlice = MathHelper.Floor((box.MinY - 2.0D) / 16.0D);
        var maxSlice = MathHelper.Floor((box.MaxY + 2.0D) / 16.0D);

        if (minSlice < 0) minSlice = 0;
        if (maxSlice >= Entities.Length) maxSlice = Entities.Length - 1;

        for (var i = minSlice; i <= maxSlice; ++i)
        {
            foreach (var entity in Entities[i])
            {
                if (entity != except && entity.BoundingBox.Intersects(box) && !entity.Dead)
                {
                    result.Add(entity);
                }
            }
        }
    }

    public virtual void CollectEntitiesOfType<T>(Box box, List<T> result) where T : Entity
    {
        var minSlice = MathHelper.Floor((box.MinY - 2.0D) / 16.0D);
        var maxSlice = MathHelper.Floor((box.MaxY + 2.0D) / 16.0D);

        if (minSlice < 0) minSlice = 0;
        if (maxSlice >= Entities.Length) maxSlice = Entities.Length - 1;

        for (var i = minSlice; i <= maxSlice; ++i)
        {
            foreach (var entity in Entities[i])
            {
                if (!entity.Dead && entity is T typedEntity && entity.BoundingBox.Intersects(box))
                {
                    result.Add(typedEntity);
                }
            }
        }
    }

    public virtual bool ShouldSave(bool saveEntities)
    {
        if (IsEmpty()) return false;

        if (saveEntities)
        {
            if (LastSaveHadEntities && World.GetTime() != LastSaveTime) return true;
        }
        else if (LastSaveHadEntities && World.GetTime() >= LastSaveTime + 600L)
        {
            return true;
        }

        return Dirty;
    }

    public virtual int LoadFromPacket(byte[] bytes, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, int offset)
    {
        var sizeX = maxX - minX;
        var sizeY = maxY - minY;
        var sizeZ = maxZ - minZ;
        var isFullChunk = sizeX == 16 && sizeY == ChuckFormat.ChunkHeight && sizeZ == 16;

        using (Profiler.Begin(isFullChunk ? "LoadChunkFull" : "LoadChunkPartial"))
        {
            for (var x = minX; x < maxX; ++x)
            {
                for (var z = minZ; z < maxZ; ++z)
                {
                    var index = ChuckFormat.GetIndex(x, minY, z);
                    Buffer.BlockCopy(bytes, offset, Blocks, index, sizeY);
                    offset += sizeY;
                }
            }

            PopulateHeightMapOnly();

            var halfSizeY = sizeY / 2;

            for (var x = minX; x < maxX; ++x)
            {
                for (var z = minZ; z < maxZ; ++z)
                {
                    var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                    Buffer.BlockCopy(bytes, offset, Meta.Bytes, index, halfSizeY);
                    offset += halfSizeY;
                }
            }

            for (var x = minX; x < maxX; ++x)
            {
                for (var z = minZ; z < maxZ; ++z)
                {
                    var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                    Buffer.BlockCopy(bytes, offset, BlockLight.Bytes, index, halfSizeY);
                    offset += halfSizeY;
                }
            }

            for (var x = minX; x < maxX; ++x)
            {
                for (var z = minZ; z < maxZ; ++z)
                {
                    var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                    Buffer.BlockCopy(bytes, offset, SkyLight.Bytes, index, halfSizeY);
                    offset += halfSizeY;
                }
            }

            for (var x = minX; x < maxX; ++x)
            {
                for (var z = minZ; z < maxZ; ++z)
                {
                    for (var y = minY; y < maxY; y++)
                    {
                        var id = GetBlockId(x, y, z);
                        if (id > 0 && World.Content.Blocks.TryGetByProtocolId(id, out var block) && block.HasBlockEntity)
                        {
                            GetBlockEntity(x, y, z);
                        }
                    }
                }
            }

            Loaded = true;
            return offset;
        }
    }

    /// <summary>
    ///     Replaces this chunk's contents from a <c>ChunkBlobCodec</c> blob.
    ///     <para>
    ///         The whole-chunk counterpart to <see cref="LoadFromPacket" />, and it does the same
    ///         three things afterwards for the same reasons: the heightmap is derived rather than
    ///         sent, block entities have to be instantiated for the blocks that declare them, and
    ///         nothing may read the chunk until <see cref="Loaded" /> says so.
    ///     </para>
    ///     <para>
    ///         Decoding writes into the existing arrays rather than replacing them, so a failed
    ///         decode leaves this chunk partially overwritten. That is deliberate and matches
    ///         <see cref="LoadFromPacket" />: the alternative is a full-chunk scratch copy on every
    ///         chunk received, and a truncated blob costs a redraw rather than correctness — the
    ///         next full send replaces it.
    ///     </para>
    /// </summary>
    public void LoadFromBlob(ReadOnlySpan<byte> blob)
    {
        using (Profiler.Begin("LoadChunkBlob"))
        {
            ChunkBlobCodec.Decode(blob, Blocks, Meta.Bytes, BlockLight.Bytes, SkyLight.Bytes);

            PopulateHeightMapOnly();

            for (var x = 0; x < 16; x++)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
                    {
                        var id = GetBlockId(x, y, z);
                        if (id > 0 && World.Content.Blocks.TryGetByProtocolId(id, out var block) && block.HasBlockEntity)
                        {
                            GetBlockEntity(x, y, z);
                        }
                    }
                }
            }

            Loaded = true;
        }
    }

    public int ToPacket(byte[] bytes, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, int offset)
    {
        var sizeX = maxX - minX;
        var sizeY = maxY - minY;
        var sizeZ = maxZ - minZ;

        if (sizeX * sizeY * sizeZ == Blocks.Length)
        {
            Buffer.BlockCopy(Blocks, 0, bytes, offset, Blocks.Length);
            offset += Blocks.Length;
            Buffer.BlockCopy(Meta.Bytes, 0, bytes, offset, Meta.Bytes.Length);
            offset += Meta.Bytes.Length;
            Buffer.BlockCopy(BlockLight.Bytes, 0, bytes, offset, BlockLight.Bytes.Length);
            offset += BlockLight.Bytes.Length;
            Buffer.BlockCopy(SkyLight.Bytes, 0, bytes, offset, SkyLight.Bytes.Length);
            return offset + SkyLight.Bytes.Length;
        }

        for (var x = minX; x < maxX; x++)
        {
            for (var z = minZ; z < maxZ; z++)
            {
                var index = ChuckFormat.GetIndex(x, minY, z);
                Buffer.BlockCopy(Blocks, index, bytes, offset, sizeY);
                offset += sizeY;
            }
        }

        var halfSizeY = sizeY / 2;

        for (var x = minX; x < maxX; x++)
        {
            for (var z = minZ; z < maxZ; z++)
            {
                var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                Buffer.BlockCopy(Meta.Bytes, index, bytes, offset, halfSizeY);
                offset += halfSizeY;
            }
        }

        for (var x = minX; x < maxX; x++)
        {
            for (var z = minZ; z < maxZ; z++)
            {
                var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                Buffer.BlockCopy(BlockLight.Bytes, index, bytes, offset, halfSizeY);
                offset += halfSizeY;
            }
        }

        for (var x = minX; x < maxX; x++)
        {
            for (var z = minZ; z < maxZ; z++)
            {
                var index = ChuckFormat.GetIndex(x, minY, z) >> 1;
                Buffer.BlockCopy(SkyLight.Bytes, index, bytes, offset, halfSizeY);
                offset += halfSizeY;
            }
        }

        return offset;
    }

    public virtual JavaRandom GetSlimeRandom(long scrambler) => new((World.Seed + X * X * 4987142 + X * 5947611 + Z * Z * 4392871L + Z * 389711) ^ scrambler);

    public virtual bool IsEmpty() => false;

    public void Fill() => BlockSource.Fill(Blocks, World.Content.Blocks);
}
