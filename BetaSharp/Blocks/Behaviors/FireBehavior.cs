using BetaSharp.Rules;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Fire: per-block-id burn/spread chance registry (vanilla Beta 1.7.3 values), age-driven
/// extinguishing, and neighbor ignition. The registry is built in <see cref="OnInit"/> rather
/// than the constructor, since it references other block statics (<c>Block.Fence</c>,
/// <c>Block.TNT</c>, ...) that may not all be assigned yet at behavior-construction time —
/// <see cref="Block.init"/> runs only after every block's static field is set, regardless of
/// declaration order.
/// </summary>
internal sealed class FireBehavior : IBlockTicker, IBlockPhysics, IBlockLifecycle
{
    private const int InitialMax = 0;
    private readonly int[] _burnChances = new int[256];
    private readonly int[] _spreadChances = new int[256];

    // ── IBlockLifecycle ───────────────────────────────────────────

    public void OnInit(Block block)
    {
        RegisterFlammableBlock(Block.Planks.id, 5, 20);
        RegisterFlammableBlock(Block.Fence.id, 5, 20);
        RegisterFlammableBlock(Block.WoodenStairs.id, 5, 20);
        RegisterFlammableBlock(Block.Log.id, 5, 5);
        RegisterFlammableBlock(Block.Leaves.id, 30, 60);
        RegisterFlammableBlock(Block.Bookshelf.id, 30, 20);
        RegisterFlammableBlock(Block.TNT.id, 15, 100);
        RegisterFlammableBlock(Block.Grass.id, 60, 100);
        RegisterFlammableBlock(Block.Wool.id, 30, 60);
    }

    private void RegisterFlammableBlock(int blockId, int burnChance, int spreadChance)
    {
        _burnChances[blockId] = burnChance;
        _spreadChances[blockId] = spreadChance;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == Block.Obsidian.id && BlockPortal.create(@event.World.Reader, @event.World.Writer, @event.X, @event.Y, @event.Z))
        {
            return;
        }

        if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) && !AreBlocksAroundFlammable(@event.World.Reader, @event.X, @event.Y, @event.Z))
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, block.getTickRate());
        }
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public bool HasCollision(Block block, bool defaultHasCollision) => false;

    public bool IsFlammable(Block block, IBlockReader reader, int x, int y, int z, bool defaultFlammable) => _burnChances[reader.GetBlockId(x, y, z)] > 0;

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) || AreBlocksAroundFlammable(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) && !AreBlocksAroundFlammable(@event.World.Reader, @event.X, @event.Y, @event.Z))
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
    }

    // ── IBlockTicker ──────────────────────────────────────────────

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (!@event.World.Rules.GetBool(DefaultRules.DoFireTick))
        {
            return;
        }

        bool isOnNetherrack = @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == Block.Netherrack.id;
        if (!block.canPlaceAt(new CanPlaceAtContext(@event.World, 0, @event.X, @event.Y, @event.Z)))
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }

        if (isOnNetherrack ||
            !@event.World.Environment.IsRaining ||
            (!@event.World.Environment.IsRainingAt(@event.X, @event.Y, @event.Z) &&
             !@event.World.Environment.IsRainingAt(@event.X - 1, @event.Y, @event.Z) &&
             !@event.World.Environment.IsRainingAt(@event.X + 1, @event.Y, @event.Z) &&
             !@event.World.Environment.IsRainingAt(@event.X, @event.Y, @event.Z - 1) &&
             !@event.World.Environment.IsRainingAt(@event.X, @event.Y, @event.Z + 1)))
        {
            int fireAge = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            if (fireAge < 15)
            {
                @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, fireAge + @event.World.Random.NextInt(3) / 2);
            }

            @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, block.getTickRate());
            if (!isOnNetherrack && !AreBlocksAroundFlammable(@event.World.Reader, @event.X, @event.Y, @event.Z))
            {
                if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) || fireAge > 3)
                {
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
                }
            }
            else if (!isOnNetherrack && !IsFlammableId(@event.World.Reader, @event.X, @event.Y - 1, @event.Z) && fireAge == 15 && @event.World.Random.NextInt(4) == 0)
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
            else
            {
                TrySpreadingFire(block, @event.World, @event.X + 1, @event.Y, @event.Z, 300, @event.World.Random, fireAge);
                TrySpreadingFire(block, @event.World, @event.X - 1, @event.Y, @event.Z, 300, @event.World.Random, fireAge);
                TrySpreadingFire(block, @event.World, @event.X, @event.Y - 1, @event.Z, 250, @event.World.Random, fireAge);
                TrySpreadingFire(block, @event.World, @event.X, @event.Y + 1, @event.Z, 250, @event.World.Random, fireAge);
                TrySpreadingFire(block, @event.World, @event.X, @event.Y, @event.Z - 1, 300, @event.World.Random, fireAge);
                TrySpreadingFire(block, @event.World, @event.X, @event.Y, @event.Z + 1, 300, @event.World.Random, fireAge);

                for (int checkX = @event.X - 1; checkX <= @event.X + 1; ++checkX)
                {
                    for (int checkZ = @event.Z - 1; checkZ <= @event.Z + 1; ++checkZ)
                    {
                        for (int checkY = @event.Y - 1; checkY <= @event.Y + 4; ++checkY)
                        {
                            if (checkX != @event.X || checkY != @event.Y || checkZ != @event.Z)
                            {
                                int spreadDifficulty = 100;
                                if (checkY > @event.Y + 1)
                                {
                                    spreadDifficulty += (checkY - (@event.Y + 1)) * 100;
                                }

                                int burnChance = GetBurnChance(@event.World.Reader, checkX, checkY, checkZ);
                                if (burnChance > 0)
                                {
                                    int spreadThreshold = (burnChance + 40) / (fireAge + 30);
                                    if (spreadThreshold > 0 &&
                                        @event.World.Random.NextInt(spreadDifficulty) <= spreadThreshold &&
                                        (!@event.World.Environment.IsRaining || !@event.World.Environment.IsRainingAt(checkX, checkY, checkZ)) &&
                                        !@event.World.Environment.IsRainingAt(checkX - 1, checkY, checkZ) &&
                                        !@event.World.Environment.IsRainingAt(checkX + 1, checkY, checkZ) &&
                                        !@event.World.Environment.IsRainingAt(checkX, checkY - 1, checkZ) &&
                                        !@event.World.Environment.IsRainingAt(checkX, checkY + 1, checkZ))
                                    {
                                        int spreadChance = fireAge + @event.World.Random.NextInt(5) / 4;
                                        if (spreadChance > 15)
                                        {
                                            spreadChance = 15;
                                        }

                                        @event.World.Writer.SetBlock(checkX, checkY, checkZ, block.id, spreadChance);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        else
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
    }

    private void TrySpreadingFire(Block block, IWorldContext level, int x, int y, int z, int spreadFactor, JavaRandom random, int currentAge)
    {
        int targetSpreadChance = _spreadChances[level.Reader.GetBlockId(x, y, z)];
        if (random.NextInt(spreadFactor) < targetSpreadChance)
        {
            bool isTnt = level.Reader.GetBlockId(x, y, z) == Block.TNT.id;
            if (random.NextInt(currentAge + 10) < 5 && !level.Environment.IsRainingAt(x, y, z))
            {
                int newFireAge = currentAge + random.NextInt(5) / 4;
                if (newFireAge > 15)
                {
                    newFireAge = 15;
                }

                level.Writer.SetBlock(x, y, z, block.id, newFireAge);
            }
            else
            {
                level.Writer.SetBlock(x, y, z, 0);
            }

            if (isTnt)
            {
                Block.TNT.onMetadataChange(new OnMetadataChangeEvent(level, x, y, z, 1));
            }
        }
    }

    private bool AreBlocksAroundFlammable(IBlockReader world, int x, int y, int z)
        => IsFlammableId(world, x + 1, y, z) ||
           IsFlammableId(world, x - 1, y, z) ||
           IsFlammableId(world, x, y - 1, z) ||
           IsFlammableId(world, x, y + 1, z) ||
           IsFlammableId(world, x, y, z - 1) ||
           IsFlammableId(world, x, y, z + 1);

    private bool IsFlammableId(IBlockReader world, int x, int y, int z) => _burnChances[world.GetBlockId(x, y, z)] > 0;

    private int GetBurnChance(IBlockReader world, int x, int y, int z)
    {
        if (!world.IsAir(x, y, z)) return 0;

        int maxChance = GetBurnChance(world, x + 1, y, z, InitialMax);
        maxChance = GetBurnChance(world, x - 1, y, z, maxChance);
        maxChance = GetBurnChance(world, x, y - 1, z, maxChance);
        maxChance = GetBurnChance(world, x, y + 1, z, maxChance);
        maxChance = GetBurnChance(world, x, y, z - 1, maxChance);
        maxChance = GetBurnChance(world, x, y, z + 1, maxChance);
        return maxChance;
    }

    private int GetBurnChance(IBlockReader world, int x, int y, int z, int currentChance)
    {
        int blockBurnChance = _burnChances[world.GetBlockId(x, y, z)];
        return blockBurnChance > currentChance ? blockBurnChance : currentChance;
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (@event.World.Random.NextInt(24) == 0)
        {
            @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5F, @event.Y + 0.5F, @event.Z + 0.5F, "fire.fire", 1.0F + Random.Shared.NextSingle(), Random.Shared.NextSingle() * 0.7F + 0.3F);
        }

        int particleIndex;
        float particleX;
        float particleY;
        float particleZ;
        if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) && !IsFlammableId(@event.World.Reader, @event.X, @event.Y - 1, @event.Z))
        {
            if (IsFlammableId(@event.World.Reader, @event.X - 1, @event.Y, @event.Z))
            {
                for (particleIndex = 0; particleIndex < 2; ++particleIndex)
                {
                    particleX = @event.X + Random.Shared.NextSingle() * 0.1F;
                    particleY = @event.Y + Random.Shared.NextSingle();
                    particleZ = @event.Z + Random.Shared.NextSingle();
                    @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }
            }

            if (IsFlammableId(@event.World.Reader, @event.X + 1, @event.Y, @event.Z))
            {
                for (particleIndex = 0; particleIndex < 2; ++particleIndex)
                {
                    particleX = @event.X + 1 - Random.Shared.NextSingle() * 0.1F;
                    particleY = @event.Y + Random.Shared.NextSingle();
                    particleZ = @event.Z + Random.Shared.NextSingle();
                    @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }
            }

            if (IsFlammableId(@event.World.Reader, @event.X, @event.Y, @event.Z - 1))
            {
                for (particleIndex = 0; particleIndex < 2; ++particleIndex)
                {
                    particleX = @event.X + Random.Shared.NextSingle();
                    particleY = @event.Y + Random.Shared.NextSingle();
                    particleZ = @event.Z + Random.Shared.NextSingle() * 0.1F;
                    @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }
            }

            if (IsFlammableId(@event.World.Reader, @event.X, @event.Y, @event.Z + 1))
            {
                for (particleIndex = 0; particleIndex < 2; ++particleIndex)
                {
                    particleX = @event.X + Random.Shared.NextSingle();
                    particleY = @event.Y + Random.Shared.NextSingle();
                    particleZ = @event.Z + 1 - Random.Shared.NextSingle() * 0.1F;
                    @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }
            }

            if (IsFlammableId(@event.World.Reader, @event.X, @event.Y + 1, @event.Z))
            {
                for (particleIndex = 0; particleIndex < 2; ++particleIndex)
                {
                    particleX = @event.X + Random.Shared.NextSingle();
                    particleY = @event.Y + 1 - Random.Shared.NextSingle() * 0.1F;
                    particleZ = @event.Z + Random.Shared.NextSingle();
                    @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                }
            }
        }
        else
        {
            for (particleIndex = 0; particleIndex < 3; ++particleIndex)
            {
                particleX = @event.X + Random.Shared.NextSingle();
                particleY = @event.Y + Random.Shared.NextSingle() * 0.5F + 0.5F;
                particleZ = @event.Z + Random.Shared.NextSingle();
                @event.World.Broadcaster.AddParticle("largesmoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
            }
        }
    }
}
