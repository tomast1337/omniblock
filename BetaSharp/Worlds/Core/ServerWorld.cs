using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Network.Messages;
using BetaSharp.Server;
using BetaSharp.Server.Internal;
using BetaSharp.Server.Worlds;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Dimensions;
using BetaSharp.Worlds.Mechanics;
using BetaSharp.Worlds.Storage;
using BetaSharp.Worlds.Storage.RegionFormat;

namespace BetaSharp.Worlds.Core;

public class ServerWorld : World
{
    private readonly Dictionary<int, Entity> entitiesById = [];
    private readonly BetaSharpServer server;
    public bool BypassSpawnProtection { get; }
    public ServerChunkCache ChunkCache;
    internal ChunkMap ChunkMap;
    public bool savingDisabled;

    public ServerWorld(BetaSharpServer server, IWorldStorage storage, string saveName, int dimensionId, WorldSettings settings, ServerWorld del) : base(storage, saveName, settings, Dimension.FromId(dimensionId))
    {
        this.server = server;
        BypassSpawnProtection = dimensionId != 0;

        Environment.OnRainingStateChanged += HandleWeatherChanged;

        Entities.OnEntityAdded += HandleEntityAdded;
        Entities.OnEntityRemoved += HandleEntityRemoved;
        Entities.OnEntityUpdating += HandleEntityUpdating;
        Entities.OnGlobalEntityAdded += HandleGlobalEntityAdded;
    }

    protected override IChunkSource CreateChunkCache()
    {
        IChunkStorage? chunkStorage = Storage.GetChunkStorage(Dimension);
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
                Z = MathHelper.Floor(entity.Z * 32.0),
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
        entitiesById.TryGetValue(id, out Entity? entity);
        return entity;
    }

    public List<BlockEntity> getBlockEntities(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
        Entities.BlockEntities
            .Where(b => b.X >= minX && b.Y >= minY && b.Z >= minZ && b.X < maxX && b.Y < maxY && b.Z < maxZ)
            .ToList();

    public override bool CanInteract(EntityPlayer player, int x, int y, int z)
    {
        int absX = Math.Abs(x - Properties.SpawnX);
        int absZ = Math.Abs(z - Properties.SpawnZ);
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
            new GameStateChangeMessage { Reason = isRaining ? (sbyte)1 : (sbyte)2 }
        );

        bool isThundering = Properties.IsThundering;
        server.playerManager.sendToAll(new GameStateChangeMessage { Reason = (sbyte)(isThundering ? 7 : 8) });
    }
}
