using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Registries;
using OmniBlock.Server;
using OmniBlock.Server.Internal;
using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Mechanics;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Worlds.Core;

public class ServerWorld : World
{
    private readonly Dictionary<int, Entity> entitiesById = [];
    private readonly OmniBlockServer server;
    public ServerChunkCache ChunkCache;
    internal ChunkMap ChunkMap;
    public bool savingDisabled;
    private ServerTerrainLodRuntime? _terrainLod;

    public ServerWorld(OmniBlockServer server, IWorldStorage storage, string saveName, int dimensionId, WorldSettings settings, ServerWorld del,
        ContentRuntime content) : base(storage, saveName, settings, Dimension.FromId(dimensionId, content), content)
    {
        this.server = server;
        BypassSpawnProtection = dimensionId != 0;

        Environment.OnRainingStateChanged += HandleWeatherChanged;

        Entities.OnEntityAdded += HandleEntityAdded;
        Entities.OnEntityRemoved += HandleEntityRemoved;
        Entities.OnEntityUpdating += HandleEntityUpdating;
        Entities.OnGlobalEntityAdded += HandleGlobalEntityAdded;

        _terrainLod = ServerTerrainLodRuntime.TryCreate(this, server.TerrainLodPolicy);
        ChunkCache.AttachTerrainLod(_terrainLod);
    }

    public bool BypassSpawnProtection { get; }
    public ServerTerrainLodSnapshot? TerrainLodSnapshot => _terrainLod?.Snapshot();
    public TerrainLodCacheIdentity? TerrainLodIdentity => _terrainLod?.Identity;

    internal void ShutdownTerrainLod()
    {
        ChunkCache.AttachTerrainLod(null);
        _terrainLod?.Shutdown();
        _terrainLod = null;
    }

    /// <summary>
    ///     Rebinds disposable distant-terrain state at the same safe server-tick boundary as the
    ///     immutable gameplay catalog. Material rules and cache identity must never outlive the
    ///     content runtime from which they were compiled.
    /// </summary>
    internal void ReplaceRuntimeContent(ContentRuntime content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (ReferenceEquals(Content, content)) return;

        ShutdownTerrainLod();
        ReplaceContent(content);
        _terrainLod = ServerTerrainLodRuntime.TryCreate(this, server.TerrainLodPolicy);
        ChunkCache.AttachTerrainLod(_terrainLod);
    }

    internal ValueTask SubmitOfflineTerrainLodAsync(
        InactiveChunkSnapshot snapshot, CancellationToken cancellationToken) =>
        _terrainLod?.SubmitOfflineAsync(snapshot, cancellationToken) ?? ValueTask.CompletedTask;

    internal TerrainLodTileAvailability GetTerrainLodCoverage(
        TerrainLodTileKey key,
        out TerrainLodColumnTile? tile)
    {
        if (_terrainLod is not null) return _terrainLod.GetSpatialCoverage(key, out tile);
        tile = null;
        return TerrainLodTileAvailability.Missing;
    }

    internal TerrainLodTileAvailability GetTerrainLodPayload(
        TerrainLodTileKey key,
        out byte[]? payload)
    {
        if (_terrainLod is not null) return _terrainLod.GetSpatialPayload(key, out payload);
        payload = null;
        return TerrainLodTileAvailability.Missing;
    }

    internal int PrepareTerrainLodScaleFixture(
        double centerChunkX,
        double centerChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks)
    {
        if (_terrainLod is null)
            throw new InvalidOperationException("The terrain LOD runtime is unavailable.");
        var surface = Content.Blocks.Get("omniblock:grass_block");
        return _terrainLod.PrepareUniformSpatialFixture(
            centerChunkX,
            centerChunkZ,
            nearDistanceChunks,
            horizonDistanceChunks,
            surface.Id);
    }

    protected override IChunkSource CreateChunkCache()
    {
        var chunkStorage = Storage.GetChunkStorage(Dimension);
        ChunkCache = new ServerChunkCache(this, chunkStorage, Dimension.CreateChunkGenerator());
        return ChunkCache;
    }

    private void HandleEntityAdded(Entity entity) => entitiesById.TryAdd(entity.ID, entity);

    private void HandleEntityRemoved(Entity entity) => entitiesById.Remove(entity.ID);

    private void HandleGlobalEntityAdded(Entity entity) =>
        server.playerManager.sendToAround(
            entity.X,
            entity.Y,
            entity.Z,
            512.0,
            Dimension.Id,
            new GlobalEntitySpawnMessage
            {
                EntityId = entity.ID,
                Type = entity.Type?.Definition is { GlobalSpawnId: > 0 } definition
                    ? (byte)definition.GlobalSpawnId
                    : (byte)0,
                X = MathHelper.Floor(entity.X * 32.0),
                Y = MathHelper.Floor(entity.Y * 32.0),
                Z = MathHelper.Floor(entity.Z * 32.0)
            });

    private bool HandleEntityUpdating(Entity entity)
    {
        // Read from the declared spawn category: no class names one, and the category is the fact
        // this check is reaching for.
        if (!server.spawnAnimals
            && entity is EntityLiving mob
            && mob.Definition.SpawnCategory is CreatureKind.CreatureCategory or CreatureKind.WaterCreatureCategory)
        {
            entity.MarkDead();
            return false;
        }

        if (entity.Passenger != null && entity.Passenger is EntityPlayer)
        {
            return false;
        }

        return true;
    }

    public Entity? getEntity(int id)
    {
        entitiesById.TryGetValue(id, out var entity);
        return entity;
    }

    public List<BlockEntity> getBlockEntities(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
        Entities.BlockEntities
            .Where(b => b.X >= minX && b.Y >= minY && b.Z >= minZ && b.X < maxX && b.Y < maxY && b.Z < maxZ)
            .ToList();

    public override bool CanInteract(EntityPlayer player, int x, int y, int z)
    {
        var absX = Math.Abs(x - Properties.SpawnX);
        var absZ = Math.Abs(z - Properties.SpawnZ);
        return absX > 16 || absZ > 16 || server.playerManager.isOperator(player.Name) || server is InternalServer;
    }

    public override Explosion CreateExplosion(Entity? source, double x, double y, double z, float power, bool fire)
    {
        Explosion explosion = new(this, source, x, y, z, power)
        {
            isFlaming = fire
        };
        explosion.doExplosionA();
        explosion.doExplosionB(false);
        var explosionMessage = new ExplosionMessage
        {
            X = x,
            Y = y,
            Z = z,
            Radius = power
        };
        explosionMessage.DestroyedBlocks.AddRange(explosion.destroyedBlockPositions);
        server.playerManager.sendToAround(x, y, z, 64.0, Dimension.Id, explosionMessage);
        return explosion;
    }

    public void forceSave() => Storage.ForceSave();

    private void HandleWeatherChanged(bool isRaining)
    {
        server.playerManager.sendToAll(
            new GameStateChangeMessage
            {
                Reason = isRaining ? (sbyte)1 : (sbyte)2
            }
        );

        var isThundering = Properties.IsThundering;
        server.playerManager.sendToAll(new GameStateChangeMessage
        {
            Reason = (sbyte)(isThundering ? 7 : 8)
        });
    }
}
