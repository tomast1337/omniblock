using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A block travelling as an entity: it falls under gravity, erases the block it left, and on
///     landing places itself back into the world, or drops as an item where it cannot sit. Which
///     block is falling is per-instance state set by whoever spawned it.
///     <para>
///         The declared wire ids map each block to its own object-spawn id (sand and gravel differ on
///         the wire), so the entity does not use the definition's single <c>SpawnObjectId</c>.
///     </para>
/// </summary>
public sealed class SettleAsBlockBehavior : IEntityTicker, IEntityPersistence
{
    private readonly StateHandle<int> _blockId;
    private readonly StateHandle<int> _fallTime;

    /// <summary>Block id to object-spawn wire id, in declaration order; the first is the fallback.</summary>
    private readonly (int BlockId, int WireId)[] _wireIds;

    public SettleAsBlockBehavior(EntityStateLayout layout, (int BlockId, int SpawnObjectId)[] wireIds)
    {
        _wireIds = wireIds;

        _blockId = layout.DeclareInt();
        _fallTime = layout.DeclareInt();
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt) => nbt.SetByte("Tile", (sbyte)self.State[_blockId]);

    public void OnReadNbt(Entity self, NBTTagCompound nbt) => self.State[_blockId] = nbt.GetByte("Tile") & 255;

    public bool OnTickEntity(Entity self)
    {
        int blockId = self.State[_blockId];
        if (blockId == 0)
        {
            self.MarkDead();
            return true;
        }

        self.PrevX = self.X;
        self.PrevY = self.Y;
        self.PrevZ = self.Z;
        ++self.State[_fallTime];
        self.VelocityY -= 0.04F;
        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
        self.VelocityX *= 0.98F;
        self.VelocityY *= 0.98F;
        self.VelocityZ *= 0.98F;
        int floorX = MathHelper.Floor(self.X);
        int floorY = MathHelper.Floor(self.Y);
        int floorZ = MathHelper.Floor(self.Z);
        if (self.World.Reader.GetBlockId(floorX, floorY, floorZ) == blockId)
        {
            self.World.Writer.SetBlock(floorX, floorY, floorZ, 0);
        }

        if (self.OnGround)
        {
            self.VelocityX *= 0.7F;
            self.VelocityZ *= 0.7F;
            self.VelocityY *= -0.5D;
            self.MarkDead();
            bool canFallThrough = Block.Blocks[blockId].Physics is FallingBlockBehavior fallingBlockPhysics
                                  && fallingBlockPhysics.CanFallThrough(new OnTickEvent(self.World, floorX, floorY - 1, floorZ, 0, blockId));
            if ((!Block.Blocks[blockId].CanPlaceAt(new CanPlaceAtContext(self.World, 0, floorX, floorY, floorZ)) || canFallThrough ||
                 !self.World.Writer.SetBlock(floorX, floorY, floorZ, blockId)) && !self.World.IsRemote)
            {
                self.DropItem(blockId, 1);
            }
        }
        else if (self.State[_fallTime] > 100 && !self.World.IsRemote)
        {
            self.DropItem(blockId, 1);
            self.MarkDead();
        }

        return true;
    }

    public int BlockId(Entity self) => self.State[_blockId];

    public void SetBlock(Entity self, int blockId) => self.State[_blockId] = blockId;

    /// <summary>The object-spawn id announcing this instance, decided by which block it carries.</summary>
    public int SpawnObjectId(Entity self)
    {
        int blockId = BlockId(self);
        foreach ((int candidate, int wireId) in _wireIds)
        {
            if (candidate == blockId)
            {
                return wireId;
            }
        }

        return _wireIds[0].WireId;
    }

    /// <summary>The block a received object-spawn id stands for, or <c>null</c> if none declared here.</summary>
    public int? BlockForSpawnObjectId(int spawnObjectId)
    {
        foreach ((int blockId, int wireId) in _wireIds)
        {
            if (wireId == spawnObjectId)
            {
                return blockId;
            }
        }

        return null;
    }
}
