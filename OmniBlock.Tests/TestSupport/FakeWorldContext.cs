using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.PathFinding;
using OmniBlock.Rules;
using OmniBlock.Server.Worlds;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Biomes.Source;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Mechanics;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.TestSupport;

public sealed class FakeWorldContext : IWorldContext
{
    private readonly World _broadcasterWorld;
    private readonly FakeChunkSource _chunkSource;
    private readonly PathFinder _pathFinder;
    private readonly PathingCoordinator _pathingRequests;

    public FakeWorldContext(ContentRuntime? content = null)
    {
        Content = content ?? ContentRuntime.Current;
        ReaderWriter = new FakeBlockGrid();
        ReaderWriter.ContentBlocks = Content.Blocks;
        Redstone = new RedstoneEngine(ReaderWriter, Content.Blocks);
        _chunkSource = new FakeChunkSource(this);
        ChunkHost = new ChunkHost(_chunkSource);
        Entities = new EntityManager(this);
        ReaderWriter.Entities = Entities;
        Dimension = new OverworldDimension();
        Dimension.SetWorld(this);
        Lighting = new LightingEngine(this);
        Environment = new EnvironmentManager(this);
        _broadcasterWorld = new BroadcasterWorldStub(Content);
        Broadcaster = new TestWorldEventBroadcaster(this, _broadcasterWorld);
        TickSchedulerSpy = new RecordingTickScheduler(this);
        Rules = new RuleSet(RuleRegistry.Instance);
        _pathFinder = new PathFinder(this);
        _pathingRequests = new PathingCoordinator(this);
    }

    public FakeBlockGrid ReaderWriter { get; }
    public RecordingTickScheduler TickSchedulerSpy { get; }

    /// <summary>Returned by <see cref="GetTime" /> for tests that need advancing world time (e.g. torch burnout history pruning).</summary>
    public long SimulatedWorldTime { get; set; }

    public ContentRuntime Content { get; private set; }

    public IBlockReader Reader => ReaderWriter;
    public IBlockWriter Writer => ReaderWriter;
    public ChunkHost ChunkHost { get; }
    public WorldEventBroadcaster Broadcaster { get; }
    public RedstoneEngine Redstone { get; }
    public EntityManager Entities { get; }
    public LightingEngine Lighting { get; }
    public EnvironmentManager Environment { get; }
    public Dimension Dimension { get; }
    public WorldTickScheduler TickScheduler => TickSchedulerSpy;
    public long Seed => 0;
    public bool IsRemote { get; set; }
    public Func<int, int, bool>? SimulationActive { get; set; }
    public RuleSet Rules { get; }
    public PersistentStateManager StateManager => throw new NotSupportedException();
    public int Difficulty { get; set; } = 1;

    /// <summary>Minimal spawn for code paths that need <see cref="EntityPlayer" /> (e.g. dispenser <c>onUse</c> tests).</summary>
    public WorldProperties Properties { get; } = new(0L, "test")
    {
        SpawnX = 0,
        SpawnY = 64,
        SpawnZ = 0
    };

    public JavaRandom Random { get; } = new(1234L);
    PathFinder IWorldContext.Pathing => _pathFinder;
    PathingCoordinator IWorldContext.PathingRequests => _pathingRequests;
    public bool IsChunkSimulationActive(int chunkX, int chunkZ) =>
        SimulationActive?.Invoke(chunkX, chunkZ) ?? true;

    public void SetDifficulty(int difficulty) => throw new NotSupportedException();
    public long GetTime() => SimulatedWorldTime;
    public int GetSpawnBlockId(int x, int z) => 0;

    /// <summary>
    ///     Delegates to the real <see cref="EntityManager" /> so entities spawned from inside game logic
    ///     (mob loot drops, slime splitting, pig-to-pigman conversion) are observable by tests.
    /// </summary>
    public bool SpawnEntity(Entity entity) => Entities.SpawnEntity(entity);

    public bool SpawnItemDrop(double x, double y, double z, ItemStack itemStack) => true;
    public bool CanInteract(EntityPlayer player, int x, int y, int z) => true;

    public Explosion CreateExplosion(Entity? source, double x, double y, double z, float power) =>
        CreateExplosion(source, x, y, z, power, false);

    public Explosion CreateExplosion(Entity? source, double x, double y, double z, float power, bool fire)
    {
        Explosion explosion = new(this, source, x, y, z, power)
        {
            isFlaming = fire
        };
        explosion.doExplosionA();
        explosion.doExplosionB(true);
        return explosion;
    }

    public void ReplaceContent(ContentRuntime content) => Content = content;

    /// <summary>
    ///     Lights the world. The default is pitch dark, which is what mob AI tests have always
    ///     assumed, so daylight is opt-in: raising this is how a test reaches the branches that only
    ///     run in the light (a spider losing interest, a monster refusing to spawn).
    /// </summary>
    public void SetLightLevel(int skyLight, int blockLight = 0)
    {
        _chunkSource.SetLightLevel(skyLight, blockLight);
        ReaderWriter.Brightness = skyLight;
    }
}

public sealed class FakeChunkSource(IWorldContext world) : IChunkSource
{
    private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];

    /// <summary>Light level stamped into every chunk, including ones created after it is set.</summary>
    public int SkyLight { get; private set; }

    public int BlockLight { get; private set; }

    public bool IsChunkLoaded(int x, int z) => true;

    public Chunk GetChunk(int x, int z)
    {
        if (_chunks.TryGetValue((x, z), out var chunk))
        {
            return chunk;
        }

        chunk = new Chunk(world, x, z)
        {
            Blocks = new byte[16 * 16 * 128],
            Meta = new ChunkNibbleArray(16 * 16 * 128),
            SkyLight = new ChunkNibbleArray(16 * 16 * 128),
            BlockLight = new ChunkNibbleArray(16 * 16 * 128)
        };
        Fill(chunk);
        _chunks[(x, z)] = chunk;
        return chunk;
    }

    public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

    public void DecorateTerrain(IChunkSource source, int x, int z)
    {
    }

    public bool Save(bool saveEntities, LoadingDisplay? display) => true;
    public bool Tick() => false;
    public bool CanSave() => false;
    public string GetDebugInfo() => "FakeChunkSource";

    public void SetLightLevel(int skyLight, int blockLight)
    {
        SkyLight = skyLight;
        BlockLight = blockLight;

        foreach (var chunk in _chunks.Values) Fill(chunk);
    }

    /// <summary>
    ///     Writes the level into both nibbles of every byte at once. A chunk holds 32768 cells per
    ///     light array, so filling them one <c>SetNibble</c> at a time would dominate test time.
    /// </summary>
    private void Fill(Chunk chunk)
    {
        Array.Fill(chunk.SkyLight.Bytes, (byte)(SkyLight * 0x11));
        Array.Fill(chunk.BlockLight.Bytes, (byte)(BlockLight * 0x11));
    }
}

public sealed class RecordingTickScheduler(IWorldContext context) : WorldTickScheduler(context)
{
    public List<(int X, int Y, int Z, int BlockId, int TickRate)> ScheduledTicks { get; } = [];

    public override void ScheduleBlockUpdate(int x, int y, int z, int blockId, int tickRate, bool instantBlockUpdateEnabled = false) => ScheduledTicks.Add((x, y, z, blockId, tickRate));
}

public sealed class TestWorldEventBroadcaster(IWorldContext ctx, World world) : WorldEventBroadcaster([], ctx.Reader, world)
{
    public override void PlayNote(int x, int y, int z, int soundType, int pitch)
    {
        var blockId = ctx.Reader.GetBlockId(x, y, z);
        if (blockId > 0)
        {
            TestBlocks.GetByProtocolId(blockId).OnBlockAction(new OnBlockActionEvent(ctx, soundType, pitch, x, y, z));
        }
    }
}

sealed file class BroadcasterWorldStub : World
{
    public BroadcasterWorldStub(ContentRuntime content) : base(new DummyWorldStorage(), "test", new WorldSettings(0L, WorldType.Default), null, content)
    {
    }

    protected override IChunkSource CreateChunkCache() => new StubChunkSource(this);
}

sealed file class StubChunkSource(IWorldContext world) : IChunkSource
{
    private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];

    public bool IsChunkLoaded(int x, int z) => true;

    public Chunk GetChunk(int x, int z)
    {
        if (_chunks.TryGetValue((x, z), out var chunk))
        {
            return chunk;
        }

        chunk = new Chunk(world, x, z)
        {
            Blocks = new byte[16 * 16 * 128],
            Meta = new ChunkNibbleArray(16 * 16 * 128),
            SkyLight = new ChunkNibbleArray(16 * 16 * 128),
            BlockLight = new ChunkNibbleArray(16 * 16 * 128)
        };
        _chunks[(x, z)] = chunk;
        return chunk;
    }

    public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

    public void DecorateTerrain(IChunkSource source, int x, int z)
    {
    }

    public bool Save(bool saveEntities, LoadingDisplay? display) => true;
    public bool Tick() => false;
    public bool CanSave() => false;
    public string GetDebugInfo() => "StubChunkSource";
}

sealed file class DummyWorldStorage : IWorldStorage
{
    public WorldProperties? LoadProperties() => null;

    public void CheckSessionLock()
    {
    }

    public IChunkStorage? GetChunkStorage(Dimension dimension) => null;

    public void Save(WorldProperties properties, List<EntityPlayer> players)
    {
    }

    public void Save(WorldProperties properties)
    {
    }

    public void ForceSave()
    {
    }

    public IPlayerStorage? GetPlayerStorage() => null;
    public FileInfo? GetWorldPropertiesFile(string name) => null;
}

public sealed class FakeBlockGrid : IBlockReader, IBlockWriter
{
    private readonly Dictionary<(int X, int Y, int Z), (int BlockId, int Meta)> _cells = [];

    public readonly List<(int X, int Y, int Z, int BlockId, int Meta)> SetBlockCalls = [];
    public readonly List<(int X, int Y, int Z, int Meta)> SetMetaCalls = [];

    /// <summary>
    ///     Sky brightness reported for every position. Defaults to pitch dark; raise it through
    ///     <see cref="FakeWorldContext.SetLightLevel" /> so the grid and the chunk light arrays agree.
    /// </summary>
    public int Brightness { get; set; }

    /// <summary>
    ///     Set by <see cref="FakeWorldContext" /> once its entity manager exists, so raycasts can
    ///     reach block shapes that consult entities.
    /// </summary>
    public EntityManager? Entities { get; set; }

    public IBlockRuntimeView ContentBlocks { get; set; } = null!;

    public BiomeSource? BiomeSource { get; set; }

    public int GetBlockId(int x, int y, int z) => _cells.TryGetValue((x, y, z), out var state) ? state.BlockId : 0;
    public int GetBlockMeta(int x, int y, int z) => _cells.TryGetValue((x, y, z), out var state) ? state.Meta : 0;

    public Material GetMaterial(int x, int y, int z)
    {
        var id = GetBlockId(x, y, z);
        return id == 0 ? Material.Air : TestBlocks.GetByProtocolId(id).Material;
    }

    public bool IsOpaque(int x, int y, int z)
    {
        var id = GetBlockId(x, y, z);
        return id != 0 && TestBlocks.IsOpaque(id);
    }

    public bool ShouldSuffocate(int x, int y, int z)
    {
        var id = GetBlockId(x, y, z);
        return id != 0 && TestBlocks.IsOpaque(id);
    }

    public BiomeSource GetBiomeSource() => BiomeSource ?? throw new NotSupportedException();
    public bool IsAir(int x, int y, int z) => GetBlockId(x, y, z) == 0;

    public int GetBrightness(int x, int y, int z) => Brightness;
    public bool IsTopY(int x, int y, int z) => false;
    public int GetTopY(int x, int z) => 0;
    public int GetTopSolidBlockY(int x, int z) => 0;
    public int GetSpawnPositionValidityY(int x, int z) => 0;

    public void MarkChunkDirty(int x, int z)
    {
    }

    public float GetVisibilityRatio(Vec3D sourcePosition, Box targetBox) => 0F;

    /// <summary>
    ///     Runs the production traversal over this grid rather than reporting a blanket miss, so
    ///     line-of-sight checks (<c>CanSee</c>, mob targeting) see real walls.
    /// </summary>
    public HitResult Raycast(Vec3D start, Vec3D end, bool includeFluids = false, bool ignoreNonSolid = false) =>
        BlockRaycaster.Cast(this, Entities!, ContentBlocks, start, end, includeFluids, ignoreNonSolid);

    public bool IsPosLoaded(int x, int y, int z) => true;

    public bool IsMaterialInBox(Box area, Func<Material, bool> predicate)
    {
        var minX = MathHelper.Floor(area.MinX);
        var maxX = MathHelper.Floor(area.MaxX);
        var minY = MathHelper.Floor(area.MinY);
        var maxY = MathHelper.Floor(area.MaxY);
        var minZ = MathHelper.Floor(area.MinZ);
        var maxZ = MathHelper.Floor(area.MaxZ);

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (predicate(GetMaterial(x, y, z)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool UpdateMovementInFluid(Box entityBox, Material fluidMaterial, Entity entity) =>
        IsMaterialInBox(entityBox, m => m == fluidMaterial);

    public event Action<int, int, int, int, int, int, int>? OnBlockChangedWithPrev;
    public event Action<int, int, int, int>? OnBlockChanged;
    public event Action<int, int, int, int>? OnNeighborsShouldUpdate;

    public bool SetBlock(int x, int y, int z, int blockId) => SetBlock(x, y, z, blockId, 0, true);

    public bool SetBlock(int x, int y, int z, int blockId, int meta) => SetBlock(x, y, z, blockId, meta, true);

    public bool SetBlock(int x, int y, int z, int blockId, int meta, bool doUpdate)
    {
        var previousBlockId = GetBlockId(x, y, z);
        var previousMeta = GetBlockMeta(x, y, z);
        _cells[(x, y, z)] = (blockId, meta);
        SetBlockCalls.Add((x, y, z, blockId, meta));
        OnBlockChangedWithPrev?.Invoke(x, y, z, previousBlockId, previousMeta, blockId, meta);
        OnBlockChanged?.Invoke(x, y, z, blockId);
        return true;
    }

    public void SetBlockMeta(int x, int y, int z, int meta)
    {
        var blockId = WriteMetaCell(x, y, z, meta);

        if (TestBlocks.IgnoresMetaUpdates(blockId & 255))
        {
            OnBlockChanged?.Invoke(x, y, z, blockId);
        }
        else
        {
            OnNeighborsShouldUpdate?.Invoke(x, y, z, blockId);
        }
    }

    public bool SetBlockWithoutCallingOnPlaced(int x, int y, int z, int blockId, int meta) => SetBlock(x, y, z, blockId, meta, true);
    public bool SetBlockWithoutNotifyingNeighbors(int x, int y, int z, int blockId, int meta) => SetBlock(x, y, z, blockId, meta, false);
    public bool SetBlockWithoutNotifyingNeighbors(int x, int y, int z, int blockId, int meta, bool notifyBlockPlaced) => SetBlock(x, y, z, blockId, meta, false);
    public bool SetBlockWithoutNotifyingNeighbors(int x, int y, int z, int blockId) => SetBlock(x, y, z, blockId, 0, false);
    public bool SetBlockWithoutNotifyingNeighbors(int x, int y, int z, int blockId, bool notifyBlockPlaced) => SetBlock(x, y, z, blockId, 0, false);

    public bool SetBlockMetaWithoutNotifyingNeighbors(int x, int y, int z, int meta)
    {
        WriteMetaCell(x, y, z, meta);
        return true;
    }

    public bool SetBlockInternal(int x, int y, int z, int id, int meta = 0) => SetBlock(x, y, z, id, meta, false);

    private int WriteMetaCell(int x, int y, int z, int meta)
    {
        var blockId = GetBlockId(x, y, z);
        _cells[(x, y, z)] = (blockId, meta);
        SetMetaCalls.Add((x, y, z, meta));
        return blockId;
    }

    public void SetInitial(int x, int y, int z, int blockId, int meta = 0) => _cells[(x, y, z)] = (blockId, meta);
}
