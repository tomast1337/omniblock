using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.TestSupport;

/// <summary>
///     A real <see cref="World" /> whose chunks appear only when a test asks for them.
/// </summary>
/// <remarks>
///     Lighting is the one system where a chunk being absent is not the same as a chunk being
///     empty: the update queue silently discards work aimed at a position whose chunk is not
///     loaded, and nothing re-queues it later. A source that materializes chunks on demand hides
///     exactly the behavior these tests exist to pin down.
/// </remarks>
public sealed class LightTestWorld : World
{
    private ControlledChunkSource _chunks = null!;

    public LightTestWorld() : base(new NoStorage(), "light", new WorldSettings(0L, WorldType.Default))
    {
    }

    public ControlledChunkSource Chunks => _chunks;

    protected override IChunkSource CreateChunkCache() => _chunks = new ControlledChunkSource(this);

    /// <summary>Runs queued light updates to completion, as a server tick eventually does.</summary>
    public void DrainLighting()
    {
        while (Lighting.DoLightingUpdates())
        {
        }
    }

    /// <summary>Sky light at a world position, with no ambient darkness applied.</summary>
    public int SkyLightAt(int x, int y, int z) => Lighting.GetBrightness(LightType.Sky, x, y, z);
}

public sealed class ControlledChunkSource(World world) : IChunkSource
{
    private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];
    private readonly Chunk _empty = new EmptyChunk(world, new byte[ChuckFormat.ChunkSize], 0, 0);

    public bool IsChunkLoaded(int x, int z) => _chunks.ContainsKey((x, z));

    public Chunk GetChunk(int x, int z) =>
        _chunks.TryGetValue((x, z), out Chunk? chunk) ? chunk : _empty;

    public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

    /// <summary>
    ///     Adds a chunk and gives it the same first light pass a generated chunk gets, in the order
    ///     <c>ServerChunkCache.LoadChunk</c> does it: terrain, then
    ///     <see cref="Chunk.PopulateHeightMap" /> for the sky light columns and the cross-border
    ///     updates, then <see cref="Chunk.PopulateBlockLight" /> once the chunk is reachable.
    /// </summary>
    /// <param name="populateLight">
    ///     Whether to run the first light pass. False leaves the chunk present and holding no light
    ///     at all, which is the state a chunk is in between arriving and being lit — the window a
    ///     test needs to reach to say anything about what happens to a reader that saw it then.
    /// </param>
    public Chunk Add(int chunkX, int chunkZ, Action<Chunk>? buildTerrain = null, bool populateLight = true)
    {
        Chunk chunk = new(world, chunkX, chunkZ)
        {
            Blocks = new byte[ChuckFormat.ChunkSize],
            Meta = new ChunkNibbleArray(ChuckFormat.ChunkSize),
            SkyLight = new ChunkNibbleArray(ChuckFormat.ChunkSize),
            BlockLight = new ChunkNibbleArray(ChuckFormat.ChunkSize)
        };

        buildTerrain?.Invoke(chunk);

        // Registered before the light pass, so the pass sees this chunk as loaded when it looks at
        // its own columns.
        _chunks[(chunkX, chunkZ)] = chunk;

        if (populateLight)
        {
            chunk.PopulateHeightMap();
            chunk.PopulateBlockLight();
        }

        chunk.Load();
        return chunk;
    }

    public void DecorateTerrain(IChunkSource source, int x, int z)
    {
    }

    public bool Save(bool saveEntities, LoadingDisplay display) => true;
    public bool Tick() => false;
    public bool CanSave() => false;
    public string GetDebugInfo() => "ControlledChunkSource";
}

public sealed class NoStorage : IWorldStorage
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
