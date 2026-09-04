using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.PathFinding;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock;

internal static class NaturalSpawner
{
    private const int SpawnMaxRadius = 8; // Expressed in chunks
    private const float SpawnMinRadius = 24.0F; // Expressed in blocks
    private const int SpawnCloseness = 6;

    private static readonly HashSet<ChunkPos> ChunksForSpawning = [];

    private static readonly string[] Monsters =
    [
        "omniblock:spider",
        "omniblock:zombie",
        "omniblock:skeleton"
    ];

    private static BlockPos GetRandomSpawningPointInChunk(IWorldContext world, PathFinder pathFinder, int centerX, int centerZ)
    {
        pathFinder.SetWorld(world.Reader);
        var x = centerX + world.Random.NextInt(16);
        var y = world.Random.NextInt(128);
        var z = centerZ + world.Random.NextInt(16);
        return new BlockPos(x, y, z);
    }

    internal static void DoSpawning(IWorldContext world, PathFinder pathFinder, bool spawnHostile, bool spawnPeaceful)
    {
        pathFinder.SetWorld(world.Reader);
        if (!spawnHostile && !spawnPeaceful) return;

        ChunksForSpawning.Clear();

        foreach (var p in world.Entities.Players)
        {
            var chunkX = MathHelper.Floor(p.X / 16.0D);
            var chunkZ = MathHelper.Floor(p.Z / 16.0D);

            for (var x = -SpawnMaxRadius; x <= SpawnMaxRadius; ++x)
            {
                for (var z = -SpawnMaxRadius; z <= SpawnMaxRadius; ++z)
                {
                    ChunksForSpawning.Add(new ChunkPos(chunkX + x, chunkZ + z));
                }
            }
        }

        var worldSpawn = world.Properties.GetSpawnPos();
        foreach (var creatureKind in CreatureKind.Values)
        {
            if (((!creatureKind.Peaceful && spawnHostile) || (creatureKind.Peaceful && spawnPeaceful)) &&
                world.Entities.CountEntitiesInCategory(creatureKind.Category) <=
                creatureKind.MobCap * ChunksForSpawning.Count / 256)
            {
                foreach (var chunk in ChunksForSpawning)
                {
                    var biome = world.Dimension.BiomeSource.GetBiome(chunk);
                    var spawnSelector = biome.GetSpawnableList(creatureKind);
                    if (spawnSelector.Empty) break;
                    var toSpawn = spawnSelector.GetNext(world.Random);

                    var spawnPos = GetRandomSpawningPointInChunk(world, pathFinder, chunk.X * 16, chunk.Z * 16);
                    if (world.Reader.ShouldSuffocate(spawnPos.X, spawnPos.Y, spawnPos.Z)) continue;
                    if (world.Reader.GetMaterial(spawnPos.X, spawnPos.Y, spawnPos.Z) != creatureKind.SpawnMaterial) continue;

                    var spawnedCount = 0;
                    var breakToNextChunk = false;

                    for (var i = 0; i < 3 && !breakToNextChunk; ++i)
                    {
                        var x = spawnPos.X;
                        var y = spawnPos.Y;
                        var z = spawnPos.Z;

                        for (var j = 0; j < 4 && !breakToNextChunk; ++j)
                        {
                            x += world.Random.NextInt(SpawnCloseness) - world.Random.NextInt(SpawnCloseness);
                            y += world.Random.NextInt(1) - world.Random.NextInt(1);
                            z += world.Random.NextInt(SpawnCloseness) - world.Random.NextInt(SpawnCloseness);
                            if (creatureKind.CanSpawnAtLocation(world.Reader, x, y, z))
                            {
                                var entityPos = new Vec3D(x + 0.5D, y, z + 0.5D);

                                if (world.Entities.GetClosestPlayer(entityPos.X, entityPos.Y, entityPos.Z, SpawnMinRadius) != null) continue;

                                if (entityPos.SquareDistanceTo((Vec3D)worldSpawn) < SpawnMinRadius * SpawnMinRadius) continue;

                                var entity = toSpawn.Factory(world);

                                entity.SetPositionAndAnglesKeepPrevAngles(entityPos.X, entityPos.Y, entityPos.Z,
                                    world.Random.NextFloat() * 360.0F, 0.0F);

                                if (entity.CanSpawn())
                                {
                                    spawnedCount++;

                                    world.Entities.SpawnEntity(entity);

                                    entity.PostSpawn();
                                    if (spawnedCount >= entity.MaxSpawnedInChunk)
                                    {
                                        breakToNextChunk = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    internal static bool SpawnMonstersAndWakePlayers(IWorldContext world, List<EntityPlayer> players)
    {
        world.Pathing.SetWorld(world.Reader);
        var monstersSpawned = false;
        foreach (var player in players)
        {
            for (var i = 0; i < 20; ++i)
            {
                var spawnX = MathHelper.Floor(player.X) + world.Random.NextInt(32) - world.Random.NextInt(32);
                var spawnZ = MathHelper.Floor(player.Z) + world.Random.NextInt(32) - world.Random.NextInt(32);
                var spawnY = MathHelper.Floor(player.Y) + world.Random.NextInt(16) - world.Random.NextInt(16);
                if (spawnY < 1)
                {
                    spawnY = 1;
                }
                else if (spawnY > ChuckFormat.WorldHeight)
                {
                    spawnY = ChuckFormat.WorldHeight;
                }

                var r = world.Random.NextInt(Monsters.Length);

                int newSpawnY;
                for (newSpawnY = spawnY; newSpawnY > 2; --newSpawnY)
                {
                    if (world.Reader.ShouldSuffocate(spawnX, newSpawnY - 1, spawnZ)) break;
                }

                while (!CreatureKind.Monster.CanSpawnAtLocation(world.Reader, spawnX, newSpawnY, spawnZ) &&
                       newSpawnY < spawnY + 16 && newSpawnY < ChuckFormat.WorldHeight)
                {
                    ++newSpawnY;
                }

                if (newSpawnY < spawnY + 16 && newSpawnY < ChuckFormat.WorldHeight)
                {
                    var entity = (EntityLiving)world.Content.EntityTypes.Create(Monsters[r], world);

                    // Feet must be on the validated air column (newSpawnY), not random spawnY — spawnY
                    // can be inside stone and collision resolution rockets mobs to the surface.
                    entity.SetPositionAndAnglesKeepPrevAngles(spawnX + 0.5D, newSpawnY, spawnZ + 0.5D,
                        world.Random.NextFloat() * 360.0F, 0.0F);
                    if (entity.CanSpawn())
                    {
                        var pathEntity = world.Pathing.FindPath(entity, player, 32.0F);
                        if (pathEntity != null && pathEntity.PathLength > 1)
                        {
                            var pathPoint = pathEntity.GetFinalPoint();
                            if (Math.Abs(pathPoint.X - player.X) < 1.5D && Math.Abs(pathPoint.Z - player.Z) < 1.5D &&
                                Math.Abs(pathPoint.Y - player.Y) < 1.5D)
                            {
                                var wakeUpPos =
                                    BedBehavior.FindWakeUpPosition(world.Reader, MathHelper.Floor(player.X),
                                        MathHelper.Floor(player.Y), MathHelper.Floor(player.Z), 1) ??
                                    new Vec3I(spawnX, newSpawnY + 1, spawnZ);

                                entity.SetPositionAndAnglesKeepPrevAngles(wakeUpPos.X + 0.5F, wakeUpPos.Y, wakeUpPos.Z + 0.5F, 0.0F, 0.0F);
                                world.Entities.SpawnEntity(entity);
                                entity.PostSpawn();
                                player.WakeUp(true, false, false);
                                entity.PlayLivingSound();
                                monstersSpawned = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        return monstersSpawned;
    }
}
