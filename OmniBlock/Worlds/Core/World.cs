using Microsoft.Extensions.Logging;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.PathFinding;
using OmniBlock.Profiling;
using OmniBlock.Registries;
using OmniBlock.Rules;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Mechanics;
using OmniBlock.Worlds.Storage;
using Silk.NET.Maths;

namespace OmniBlock.Worlds.Core;

public abstract class World : IWorldContext
{
    private static readonly int s_autosavePeriod = 40;

    private readonly HashSet<ChunkPos> _activeChunks = new();
    private readonly ILogger<World> _logger = Log.Instance.For<World>();

    public readonly Dimension Dimension;

    private int _lcgBlockSeed = System.Random.Shared.Next();
    private int _soundCounter = System.Random.Shared.Next(12000);
    private bool _spawnHostileMobs = true;
    private bool _spawnPeacefulMobs = true;

    protected int AutosavePeriod = s_autosavePeriod;
    public bool IsNewWorld;

    protected World(IWorldStorage worldStorage, string levelName, WorldSettings settings, Dimension? dim,
        ContentRuntime content)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Pathing = new PathFinder(this);
        Storage = worldStorage;
        StateManager = new PersistentStateManager(worldStorage);

        var loadedProperties = worldStorage.LoadProperties();
        var shouldInitializeSpawn = loadedProperties == null;
        Properties = loadedProperties ?? new WorldProperties(settings, levelName);
        // Storage and transitional callers may still carry a legacy WorldType instance. Worlds
        // always bind that stable key back to the immutable type owned by their injected runtime.
        Properties.TerrainType = Content.WorldTypes.Get(Properties.TerrainType.Key);
        if (Properties.ContentManifest is { } savedManifest)
        {
            var compatibility = Content.Manifest.CompareTo(savedManifest);
            if (!compatibility.CanLoadWorld)
                throw new InvalidOperationException($"World content catalog is incompatible: {compatibility.Diagnostic}");
        }

        Properties.ContentManifest = Content.Manifest;

        if (dim != null)
        {
            Dimension = dim;
        }
        else if (Properties.Dimension == -1)
        {
            Dimension = Dimension.FromId(-1, Content);
        }
        else
        {
            Dimension = Dimension.FromId(0, Content);
        }


        if (Dimension is OverworldDimension && Properties.TerrainType.Key == WorldType.Sky.Key)
        {
            Dimension = new SkyDimension();
        }

        shouldInitializeSpawn = shouldInitializeSpawn && this is ServerWorld && Dimension.Id == 0;

        Dimension.SetWorld(this);

        var chunkSource = CreateChunkCache();

        Random = new JavaRandom();
        Rules = Properties.RulesTag != null
            ? RuleSet.FromNBT(RuleRegistry.Instance, Properties.RulesTag)
            : new RuleSet(RuleRegistry.Instance);

        BlockHost = new ChunkHost(chunkSource);
        Reader = new WorldReader(this, Dimension);
        // Constructed here, not with Pathing above: PathFinder captures world.Reader once at
        // construction and (unlike Pathing) is never re-primed via SetWorld before use, so it
        // needs Reader to already be assigned.
        PathingRequests = new PathingCoordinator(this);
        Writer = new WorldWriter(BlockHost, Reader, Content.Blocks);
        Writer.OnBlockChanged += BlockUpdate;

        Broadcaster = new WorldEventBroadcaster(EventListeners, Reader, this);

        Writer.OnNeighborsShouldUpdate += (x, y, z, id) => Broadcaster.NotifyNeighbors(x, y, z, id);

        Redstone = new RedstoneEngine(Reader, Content.Blocks);
        Lighting = new LightingEngine(this);
        Lighting.OnLightUpdated += (x, y, z) => Broadcaster.BlockUpdateEvent(x, y, z);
        TickScheduler = new WorldTickScheduler(this);
        Environment = new EnvironmentManager(this);
        Entities = new EntityManager(this);

        Entities.OnBlockUpdateRequired += (x, y, z) => Broadcaster.BlockUpdateEvent(x, y, z);

        Environment.PrepareWeather();
        Environment.UpdateSkyBrightness();

        Entities.OnEntityAdded += ent =>
        {
            for (var i = 0; i < EventListeners.Count; ++i)
            {
                EventListeners[i].NotifyEntityAdded(ent);
            }
        };
        Entities.OnEntityRemoved += ent =>
        {
            for (var i = 0; i < EventListeners.Count; ++i)
            {
                EventListeners[i].NotifyEntityRemoved(ent);
            }
        };


        if (shouldInitializeSpawn)
        {
            InitializeSpawnPoint();
        }
    }

    /// <summary>
    ///     Whether this world is currently searching for its spawn point, during which reading a
    ///     chunk is allowed to generate it.
    /// </summary>
    /// <remarks>
    ///     Normally a read of an absent chunk yields an empty one rather than generating it, so that
    ///     lighting, entity AI and block queries near a border cannot pull new terrain into
    ///     existence just by looking at it — anything that genuinely wants a chunk asks
    ///     <c>LoadChunk</c> for one. The spawn search is the exception: it probes candidate
    ///     positions and has nothing else to ask.
    /// </remarks>
    public bool IsFindingSpawnPoint { get; private set; }

    public ChunkHost BlockHost { get; }
    public WorldEventBroadcaster Broadcaster { get; }

    public EntityManager Entities { get; }
    public EnvironmentManager Environment { get; }

    public List<IWorldEventListener> EventListeners { get; } = [];
    public LightingEngine Lighting { get; }

    private PathFinder Pathing { get; }
    private PathingCoordinator PathingRequests { get; }
    private RedstoneEngine Redstone { get; }
    protected IWorldStorage Storage { get; }
    public long Seed => Properties.RandomSeed;
    public ContentRuntime Content { get; private set; }
    public IBlockReader Reader { get; }
    public IBlockWriter Writer { get; }
    public WorldTickScheduler TickScheduler { get; }
    public int Difficulty { get; private set; }

    public PersistentStateManager StateManager { get; protected init; }

    public WorldProperties Properties { get; protected init; }
    public bool IsRemote { get; init; }
    public JavaRandom Random { get; }
    public int SimulationDistance { get; private set; } = 9;

    public void SetSimulationDistance(int chunks) =>
        SimulationDistance = Math.Clamp(chunks, 2, 32);

    public bool IsChunkSimulationActive(int chunkX, int chunkZ) =>
        IsRemote || _activeChunks.Contains(new ChunkPos(chunkX, chunkZ));

    ChunkHost IWorldContext.ChunkHost => BlockHost;
    WorldEventBroadcaster IWorldContext.Broadcaster => Broadcaster;
    RedstoneEngine IWorldContext.Redstone => Redstone;
    EntityManager IWorldContext.Entities => Entities;
    LightingEngine IWorldContext.Lighting => Lighting;
    EnvironmentManager IWorldContext.Environment => Environment;
    Dimension IWorldContext.Dimension => Dimension;
    long IWorldContext.Seed => Properties.RandomSeed;
    PathFinder IWorldContext.Pathing => Pathing;
    PathingCoordinator IWorldContext.PathingRequests => PathingRequests;

    public RuleSet Rules { get; protected set; }

    /// <summary>
    ///     Adds an entity to the world, by whichever of the two routes its type calls for.
    /// </summary>
    /// <remarks>
    ///     A type declaring a <c>GlobalSpawnId</c> is announced to every nearby client rather than
    ///     tracked per chunk, and only <see cref="EntityManager.SpawnGlobalEntity" /> raises the
    ///     event that announces it. Choosing here rather than at each call site is what stops a
    ///     caller silently getting the wrong one: a lightning bolt spawned the ordinary way still
    ///     ticks, still lights fires and still makes no sound and no picture, because the client is
    ///     never told it exists.
    /// </remarks>
    public bool SpawnEntity(Entity entity) =>
        entity.Type?.Definition is { GlobalSpawnId: > 0 }
            ? Entities.SpawnGlobalEntity(entity)
            : Entities.SpawnEntity(entity);

    public bool SpawnItemDrop(double x, double y, double z, ItemStack itemStack)
    {
        var droppedItem = DroppedItemBehavior.Create(this, x, y, z, itemStack, 10);
        return Entities.SpawnEntity(droppedItem);
    }

    public Explosion CreateExplosion(Entity? source, double x, double y, double z, float power) => CreateExplosion(source, x, y, z, power, false);

    public virtual Explosion CreateExplosion(Entity? source, double x, double y, double z, float power, bool fire)
    {
        Explosion explosion = new(this, source, x, y, z, power)
        {
            isFlaming = fire
        };
        explosion.doExplosionA();
        explosion.doExplosionB(true);
        return explosion;
    }

    public long GetTime() => Properties.WorldTime;

    public void SetDifficulty(int difficulty) => Difficulty = difficulty;

    public virtual bool CanInteract(EntityPlayer player, int x, int y, int z) => true;

    public int GetSpawnBlockId(int x, int z)
    {
        int y;
        for (y = 63; !Reader.IsAir(x, y + 1, z); ++y)
        {
        }

        return Reader.GetBlockId(x, y, z);
    }

    public void ReplaceContent(ContentRuntime content) =>
        Content = content ?? throw new ArgumentNullException(nameof(content));

    public BiomeSource GetBiomeSource() => Dimension.BiomeSource;

    public float GetLuminance(int x, int y, int z) => Lighting.GetLuminance(x, y, z);

    public IWorldStorage GetWorldStorage() => Storage;

    protected abstract IChunkSource CreateChunkCache();

    private void InitializeSpawnPoint()
    {
        IsFindingSpawnPoint = true;
        try
        {
            var x = 0;
            var z = 0;
            var y = 64;

            const int maxAttempts = 512;
            var attempts = 0;

            while (!Dimension.IsValidSpawnPoint(x, z) && attempts++ < maxAttempts)
            {
                x += Random.NextInt(64) - Random.NextInt(64);
                z += Random.NextInt(64) - Random.NextInt(64);
            }

            if (!Dimension.IsValidSpawnPoint(x, z))
            {
                x = 0;
                z = 0;
                UpdateSpawnPosition();
                x = Properties.SpawnX;
                z = Properties.SpawnZ;
            }

            if (Properties.TerrainType.Key == WorldType.Sky.Key)
            {
                var topY = Reader.GetTopSolidBlockY(x, z);
                if (topY > 0)
                {
                    y = topY;
                }
            }

            Properties.SetSpawn(x, y, z);
        }
        finally
        {
            IsFindingSpawnPoint = false;
        }
    }

    public virtual void UpdateSpawnPosition()
    {
        if (Properties.SpawnY <= 0)
        {
            Properties.SpawnY = 64;
        }

        var spawnX = Properties.SpawnX;

        int spawnZ;
        for (spawnZ = Properties.SpawnZ;
             GetSpawnBlockId(spawnX, spawnZ) == 0;
             spawnZ += Random.NextInt(8) - Random.NextInt(8))
        {
            spawnX += Random.NextInt(8) - Random.NextInt(8);
        }

        Properties.SpawnX = spawnX;
        Properties.SpawnZ = spawnZ;
    }

    public static void SaveWorldData()
    {
    }

    public void AddPlayer(EntityPlayer player)
    {
        try
        {
            var tag = Properties.PlayerTag;
            if (tag != null)
            {
                player.Read(tag);
                Properties.PlayerTag = null;
            }

            Entities.SpawnEntity(player);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
        }
    }

    public void SaveWithLoadingDisplay(bool saveEntities, LoadingDisplay? loadingDisplay)
    {
        if (!BlockHost.ChunkSource.CanSave()) return;

        if (loadingDisplay != null)
        {
            loadingDisplay.BeginLoadingPersistent("Saving level");
        }

        using (Profiler.Begin("SaveLevel"))
        {
            Save();
        }

        if (loadingDisplay != null)
        {
            loadingDisplay.SetStage("Saving chunks");
        }

        using (Profiler.Begin("SaveChunks"))
        {
            BlockHost.ChunkSource.Save(saveEntities, loadingDisplay);
        }
    }

    private void Save()
    {
        using (Profiler.Begin("SaveWorldInfo"))
        {
            Properties.RulesTag = new NBTTagCompound();
            Rules.WriteToNBT(Properties.RulesTag);
            Storage.Save(Properties, Entities.Players.ToList());
        }

        using (Profiler.Begin("SaveAllData"))
        {
            StateManager.SaveAllData();
        }
    }

    public bool AttemptSaving(int i)
    {
        if (!BlockHost.ChunkSource.CanSave())
        {
            return true;
        }

        if (i == 0)
        {
            Save();
        }

        return BlockHost.ChunkSource.Save(false, null);
    }

    public float GetTime(float partialTicks) => Dimension.GetTimeOfDay(Properties.WorldTime, partialTicks);

    protected void BlockUpdate(int x, int y, int z, int blockId)
    {
        Broadcaster.BlockUpdateEvent(x, y, z);
        Broadcaster.NotifyNeighbors(x, y, z, blockId);
    }

    public Vector3D<double> GetFogColor(float partialTicks)
    {
        var timeOfDay = GetTime(partialTicks);
        return Dimension.GetFogColor(timeOfDay, partialTicks);
    }

    public float CalculateSkyLightIntensity(float partialTicks)
    {
        var timeOfDay = GetTime(partialTicks);
        var intensityFactor = 1.0F - (MathHelper.Cos(timeOfDay * (float)Math.PI * 2.0F) * 2.0F + 12.0F / 16.0F);
        intensityFactor = Math.Clamp(intensityFactor, 0.0F, 1.0F);

        return intensityFactor * intensityFactor * 0.5F;
    }

    public bool IsMaterialInBox(Box area, Func<Material, bool> predicate) => Reader.IsMaterialInBox(area, predicate);

    public void ExtinguishFire(EntityPlayer? player, int x, int y, int z, int direction)
    {
        if (direction == 0)
        {
            --y;
        }

        if (direction == 1)
        {
            ++y;
        }

        switch (direction)
        {
            case 2:
                --z;
                break;
            case 3:
                ++z;
                break;
            case 4:
                --x;
                break;
            case 5:
                ++x;
                break;
        }

        if (Reader.GetBlockId(x, y, z) == Content.Blocks.Get("fire").Id)
        {
            Broadcaster.WorldEvent(player, 1004, x, y, z, 0);
            Writer.SetBlock(x, y, z, 0);
        }
    }

    public static Entity? GetPlayerForProxy(Type type) => null;

    public string GetDebugInfo() => BlockHost.ChunkSource.GetDebugInfo();

    public void SavingProgress(LoadingDisplay display) => SaveWithLoadingDisplay(true, display);

    public void allowSpawning(bool allowMonsterSpawning, bool allowMobSpawning)
    {
        _spawnHostileMobs = allowMonsterSpawning;
        _spawnPeacefulMobs = allowMobSpawning;
    }

    public virtual void Tick()
    {
        RefreshActiveSimulationChunks();
        Environment.UpdateWeatherCycles();

        long nextWorldTime;

        if (!IsRemote && Entities.AreAllPlayersAsleep())
        {
            var wasSpawnInterrupted = false;

            if (_spawnHostileMobs && Difficulty >= 1)
            {
                wasSpawnInterrupted = NaturalSpawner.SpawnMonstersAndWakePlayers(this, Entities.Players);
            }

            if (!wasSpawnInterrupted)
            {
                Environment.SkipNightAndClearWeather();
                Entities.WakeAllPlayers();
            }
        }

        using (Profiler.Begin("PerformSpawning"))
        {
            NaturalSpawner.DoSpawning(
                this, Pathing, _spawnHostileMobs, _spawnPeacefulMobs,
                Math.Min(NaturalSpawner.SpawnMaxRadius, SimulationDistance));
        }

        using (Profiler.Begin("UnloadOldChunks"))
        {
            BlockHost.ChunkSource.Tick();
        }

        using (Profiler.Begin("UpdateSkylight"))
        {
            var currentAmbientDarkness = Environment.GetAmbientDarkness(1.0F);
            if (currentAmbientDarkness != Environment.AmbientDarkness)
            {
                Environment.AmbientDarkness = currentAmbientDarkness;

                for (var i = 0; i < EventListeners.Count; ++i)
                {
                    EventListeners[i].NotifyAmbientDarknessChanged();
                }
            }
        }

        nextWorldTime = Properties.WorldTime + 1L;
        if (nextWorldTime % AutosavePeriod == 0L)
        {
            using (Profiler.Begin("Autosave"))
            {
                SaveWithLoadingDisplay(false, null);
            }
        }

        Properties.WorldTime = nextWorldTime;

        using (Profiler.Begin("TickScheduler"))
        {
            TickScheduler.Tick();
        }

        ManageChunkUpdatesAndEvents();
    }

    protected virtual void ManageChunkUpdatesAndEvents()
    {
        if (_soundCounter > 0)
        {
            --_soundCounter;
        }

        foreach (var chunkPos in _activeChunks)
        {
            var worldXBase = chunkPos.X * 16;
            var worldZBase = chunkPos.Z * 16;
            var currentChunk = BlockHost.GetChunk(chunkPos.X, chunkPos.Z);

            if (_soundCounter == 0)
            {
                _lcgBlockSeed = _lcgBlockSeed * 3 + 1013904223;
                var randomVal = _lcgBlockSeed >> 2;
                var localX = randomVal & 15;
                var localZ = (randomVal >> 8) & 15;
                var localY = (randomVal >> 16) & 127;

                var blockId = currentChunk.GetBlockId(localX, localY, localZ);
                var worldX = localX + worldXBase;
                var worldZ = localZ + worldZBase;
                if (blockId == 0 && Reader.GetBrightness(worldX, localY, worldZ) <= Random.NextInt(8) &&
                    Lighting.GetBrightness(LightType.Sky, worldX, localY, worldZ) <= 0)
                {
                    var closest = Entities.GetClosestPlayer(worldX + 0.5D, localY + 0.5D, worldZ + 0.5D, 8.0D);
                    if (closest != null &&
                        closest.GetSquaredDistance(worldX + 0.5D, localY + 0.5D, worldZ + 0.5D) > 4.0D)
                    {
                        Broadcaster.PlaySoundAtPos(worldX + 0.5D, localY + 0.5D, worldZ + 0.5D, "ambient.cave.cave", 0.7F,
                            0.8F + Random.NextFloat() * 0.2F);
                        _soundCounter = Random.NextInt(12000) + 6000;
                    }
                }
            }

            if (Random.NextInt(100000) == 0 && Environment.IsRaining && Environment.IsThundering())
            {
                _lcgBlockSeed = _lcgBlockSeed * 3 + 1013904223;
                var randomVal = _lcgBlockSeed >> 2;
                var worldX = worldXBase + (randomVal & 15);
                var worldZ = worldZBase + ((randomVal >> 8) & 15);
                var worldY = Reader.GetTopSolidBlockY(worldX, worldZ);

                if (Environment.IsRainingAt(worldX, worldY, worldZ))
                {
                    var bolt = Content.EntityTypes.Create("omniblock:lightningbolt", this);
                    bolt.SetPositionAndAnglesKeepPrevAngles(worldX, worldY, worldZ, 0.0F, 0.0F);
                    Entities.SpawnGlobalEntity(bolt);
                    Environment.LightningTicksLeft = 2;
                }
            }

            if (Random.NextInt(16) == 0)
            {
                _lcgBlockSeed = _lcgBlockSeed * 3 + 1013904223;
                var randomVal = _lcgBlockSeed >> 2;
                var localX = randomVal & 15;
                var localZ = (randomVal >> 8) & 15;
                var worldX = localX + worldXBase;
                var worldZ = localZ + worldZBase;
                var worldY = Reader.GetTopSolidBlockY(worldX, worldZ);

                if (GetBiomeSource().GetBiome(worldX, worldZ).GetEnableSnow() && worldY >= 0 && worldY < ChuckFormat.WorldHeight &&
                    currentChunk.GetLight(LightType.Block, localX, worldY, localZ) < 10)
                {
                    var blockBelowId = currentChunk.GetBlockId(localX, worldY - 1, localZ);
                    var currentBlockId = currentChunk.GetBlockId(localX, worldY, localZ);

                    if (Environment.IsRaining && currentBlockId == 0 && Content.Blocks.Get("snow").CanPlaceAt(new CanPlaceAtContext(this, 1.ToSide(), worldX, worldY, worldZ)) &&
                        blockBelowId != 0 && blockBelowId != Content.Blocks.Get("ice").Id &&
                        Content.Blocks.GetByProtocolId(blockBelowId).Material.BlocksMovement)
                    {
                        Writer.SetBlock(worldX, worldY, worldZ, Content.Blocks.Get("snow").Id);
                    }

                    if (blockBelowId == Content.Blocks.Get("water").Id && currentChunk.GetBlockMeta(localX, worldY - 1, localZ) == 0)
                    {
                        Writer.SetBlock(worldX, worldY - 1, worldZ, Content.Blocks.Get("ice").Id);
                    }
                }
            }

            for (var j = 0; j < 80; ++j)
            {
                _lcgBlockSeed = _lcgBlockSeed * 3 + 1013904223;
                var randomTickVal = _lcgBlockSeed >> 2;
                var localX = randomTickVal & 15;
                var localZ = (randomTickVal >> 8) & 15;
                var localY = (randomTickVal >> 16) & 127;

                RandomTickBlock(currentChunk, localX, localY, localZ, worldXBase, worldZBase);
            }
        }
    }

    private void RefreshActiveSimulationChunks()
    {
        _activeChunks.Clear();
        for (var i = 0; i < Entities.Players.Count; ++i)
        {
            var player = Entities.Players[i];
            var playerChunkX = MathHelper.Floor(player.X / 16.0D);
            var playerChunkZ = MathHelper.Floor(player.Z / 16.0D);
            for (var xOffset = -SimulationDistance; xOffset <= SimulationDistance; ++xOffset)
            for (var zOffset = -SimulationDistance; zOffset <= SimulationDistance; ++zOffset)
                _activeChunks.Add(new ChunkPos(xOffset + playerChunkX, zOffset + playerChunkZ));
        }
    }

    internal void RandomTickBlock(Chunk chunk, int localX, int localY, int localZ, int worldXBase, int worldZBase)
    {
        var blockId = chunk.GetBlockId(localX, localY, localZ);
        if (!Content.Blocks.TryGetByProtocolId(blockId, out var block) || !block.TickRandomly) return;

        block.OnTick(new OnTickEvent(this, localX + worldXBase, localY, localZ + worldZBase,
            chunk.GetBlockMeta(localX, localY, localZ), blockId));
    }

    public void displayTick(int centerX, int centerY, int centerZ)
    {
        const byte searchRadius = 16;

        for (var i = 0; i < 1000; ++i)
        {
            var targetX = centerX + Random.NextInt(searchRadius) - Random.NextInt(searchRadius);
            var targetY = centerY + Random.NextInt(searchRadius) - Random.NextInt(searchRadius);
            var targetZ = centerZ + Random.NextInt(searchRadius) - Random.NextInt(searchRadius);

            var blockId = Reader.GetBlockId(targetX, targetY, targetZ);
            if (blockId > 0)
            {
                Content.Blocks.GetByProtocolId(blockId).RandomDisplayTick(new OnTickEvent(this, targetX, targetY, targetZ, Reader.GetBlockMeta(targetX, targetY, targetZ), blockId));
            }
        }
    }

    public void TickChunks()
    {
        while (BlockHost.ChunkSource.Tick())
        {
        }
    }


    /// <summary>
    ///     Applies a whole chunk received as a <c>ChunkBlobCodec</c> blob.
    ///     <para>
    ///         The counterpart to <see cref="HandleChunkDataUpdate" /> for the full-chunk path. That
    ///         one exists to walk an arbitrary box across chunk boundaries, which a whole chunk never
    ///         does, so this is the same work without the four-way clamping.
    ///     </para>
    /// </summary>
    public void ApplyChunkBlob(int chunkX, int chunkZ, ReadOnlySpan<byte> blob)
    {
        BlockHost.GetChunk(chunkX, chunkZ).LoadFromBlob(blob);

        setBlocksDirty(
            chunkX * 16, 0, chunkZ * 16,
            InclusiveUpdateEnd(chunkX * 16, 16),
            InclusiveUpdateEnd(0, ChuckFormat.WorldHeight),
            InclusiveUpdateEnd(chunkZ * 16, 16),
            true);
    }

    public void HandleChunkDataUpdate(int x, int y, int z, int sizeX, int sizeY, int sizeZ, byte[] chunkData)
    {
        var startChunkX = x >> 4;
        var startChunkZ = z >> 4;
        var endChunkX = (x + sizeX - 1) >> 4;
        var endChunkZ = (z + sizeZ - 1) >> 4;

        var currentBufferOffset = 0;
        var minY = Math.Max(0, y);
        var maxY = Math.Min(ChuckFormat.WorldHeight, y + sizeY);

        for (var chunkX = startChunkX; chunkX <= endChunkX; ++chunkX)
        {
            var localStartX = Math.Max(0, x - chunkX * 16);
            var localEndX = Math.Min(16, x + sizeX - chunkX * 16);

            for (var chunkZ = startChunkZ; chunkZ <= endChunkZ; ++chunkZ)
            {
                var localStartZ = Math.Max(0, z - chunkZ * 16);
                var localEndZ = Math.Min(16, z + sizeZ - chunkZ * 16);

                currentBufferOffset = BlockHost.GetChunk(chunkX, chunkZ).LoadFromPacket(
                    chunkData,
                    localStartX, minY, localStartZ,
                    localEndX, maxY, localEndZ,
                    currentBufferOffset);

                setBlocksDirty(
                    chunkX * 16 + localStartX, minY, chunkZ * 16 + localStartZ,
                    InclusiveUpdateEnd(chunkX * 16 + localStartX, localEndX - localStartX),
                    InclusiveUpdateEnd(minY, maxY - minY),
                    InclusiveUpdateEnd(chunkZ * 16 + localStartZ, localEndZ - localStartZ),
                    true);
            }
        }
    }

    /// <summary>
    ///     Packet and blob dimensions are counts, while world dirty notifications use inclusive
    ///     maxima. Keeping that conversion explicit prevents a 16-cell chunk update from claiming
    ///     cell 16 in the neighboring chunk (and height 128 above the world).
    /// </summary>
    internal static int InclusiveUpdateEnd(int start, int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        return checked(start + length - 1);
    }

    public virtual void Disconnect()
    {
    }

    public void SetTime(long time) => Properties.WorldTime = time;

    public long GetSeed() => Properties.RandomSeed;

    public void SetSpawnPos(Vec3I pos) => Properties.SetSpawn(pos.X, pos.Y, pos.Z);

    public void setBlocksDirty(
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        bool streaming = false)
    {
        for (var i = 0; i < EventListeners.Count; ++i)
        {
            if (streaming)
                EventListeners[i].SetBlocksDirtyForStreaming(minX, minY, minZ, maxX, maxY, maxZ);
            else
                EventListeners[i].SetBlocksDirty(minX, minY, minZ, maxX, maxY, maxZ);
        }
    }

    public void setLightDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        for (var i = 0; i < EventListeners.Count; ++i)
            EventListeners[i].SetLightDirty(minX, minY, minZ, maxX, maxY, maxZ);
    }
}
