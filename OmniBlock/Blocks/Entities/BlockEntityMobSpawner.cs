using Microsoft.Extensions.Logging;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Entities;

public class BlockEntityMobSpawner : BlockEntity
{
    private readonly ILogger<BlockEntityMobSpawner> _logger = Log.Instance.For<BlockEntityMobSpawner>();
    private string _spawnedEntityId = "Pig";

    public BlockEntityMobSpawner()
    {
        SpawnDelay = 20;
    }

    protected override BlockEntityType Type => MobSpawner;
    public int SpawnDelay { get; set; } = -1;
    public double Rotation { get; set; }
    public double LastRotation { get; set; }

    public string GetSpawnedEntityId()
    {
        return _spawnedEntityId;
    }

    public void SetSpawnedEntityId(string spawnedEntityId)
    {
        _spawnedEntityId = spawnedEntityId;
    }

    private bool IsPlayerInRange()
    {
        return World!.Entities.GetClosestPlayer(X + 0.5D, Y + 0.5D, Z + 0.5D, 16.0D) != null;
    }

    public override void Tick(EntityManager entities)
    {
        LastRotation = Rotation;
        if (!IsPlayerInRange()) return;

        double particleX = X + Random.Shared.NextSingle();
        double particleY = Y + Random.Shared.NextSingle();
        double particleZ = Z + Random.Shared.NextSingle();
        World!.Broadcaster.AddParticle("smoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
        World!.Broadcaster.AddParticle("flame", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);

        for (Rotation += 1000.0F / (SpawnDelay + 200.0F); Rotation > 360.0D; LastRotation -= 360.0D) Rotation -= 360.0D;

        if (!World!.IsRemote)
        {
            if (SpawnDelay == -1) ResetDelay();

            if (SpawnDelay > 0)
            {
                --SpawnDelay;
                return;
            }

            byte max = 4;

            for (var spawnAttempt = 0; spawnAttempt < max; ++spawnAttempt)
            {
                var entityLiving = (EntityLiving?)EntityRegistry.Create(_spawnedEntityId, World);
                if (entityLiving == null) return;

                var count = World!.Entities
                    .CollectEntitiesOfType<EntityLiving>(new Box(X, Y, Z, X + 1, Y + 1, Z + 1)
                        .Expand(8.0D, 4.0D, 8.0D))
                    .Count(e => e.GetType() == entityLiving.GetType());
                if (count >= 6)
                {
                    ResetDelay();
                    return;
                }

                var posX = X + (World!.Random.NextDouble() - World!.Random.NextDouble()) * 4.0D;
                double posY = Y + World!.Random.NextInt(3) - 1;
                var posZ = Z + (World!.Random.NextDouble() - World!.Random.NextDouble()) * 4.0D;
                entityLiving.SetPositionAndAnglesKeepPrevAngles(posX, posY, posZ, World!.Random.NextFloat() * 360.0F, 0.0F);
                if (!entityLiving.CanSpawn()) continue;

                World!.SpawnEntity(entityLiving);

                for (var particleIndex = 0; particleIndex < 20; ++particleIndex)
                {
                    particleX = X + 0.5D + (World!.Random.NextFloat() - 0.5D) * 2.0D;
                    particleY = Y + 0.5D + (World!.Random.NextFloat() - 0.5D) * 2.0D;
                    particleZ = Z + 0.5D + (World!.Random.NextFloat() - 0.5D) * 2.0D;
                    World!.Broadcaster.AddParticle("smoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                    World!.Broadcaster.AddParticle("flame", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }

                entityLiving.AnimateSpawn();
                ResetDelay();
            }
        }

        base.Tick(entities);
    }

    private void ResetDelay()
    {
        SpawnDelay = 200 + World!.Random.NextInt(600);
        _logger.LogInformation("Spawn Delay: " + SpawnDelay);
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        _spawnedEntityId = nbt.GetString("EntityId");
        SpawnDelay = nbt.GetShort("Delay");
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        nbt.SetString("EntityId", _spawnedEntityId);
        nbt.SetShort("Delay", (short)SpawnDelay);
    }
}