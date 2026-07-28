using BetaSharp.Blocks;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     An arrow in flight and at rest: it arcs like a throwable but survives arrival — a block hit
///     buries the tip and starts the shake, an armored hit bounces it off backwards, and a
///     player-owned arrow stuck in the ground can be pulled back out. The rest of the game asks two
///     questions of it, exposed as statics: <see cref="IsArrow" /> for the damage-scaling rules and
///     <see cref="OwnerOf" /> to credit the hit to whoever loosed it.
/// </summary>
public sealed class ArrowBehavior : IEntityTicker, IEntityPersistence, IEntityInteractable, IEntityPhysics
{
    private readonly StateHandle<EntityLiving> _owner;
    private readonly StateHandle<bool> _belongsToPlayer;
    private readonly StateHandle<bool> _inGround;
    private readonly StateHandle<int> _inTile;
    private readonly StateHandle<int> _inData;
    private readonly StateHandle<int> _shake;
    private readonly StateHandle<int> _ticksInAir;
    private readonly StateHandle<int> _ticksInGround;
    private readonly StateHandle<int> _tileX;
    private readonly StateHandle<int> _tileY;
    private readonly StateHandle<int> _tileZ;

    private readonly int _damage;

    public ArrowBehavior(in EntityBehaviorContext context)
    {
        _damage = context.Int("damage", 4);

        _owner = context.DeclareRef<EntityLiving>();
        _belongsToPlayer = context.DeclareBool();
        _inGround = context.DeclareBool();
        _inTile = context.DeclareInt();
        _inData = context.DeclareInt();
        _shake = context.DeclareInt();
        _ticksInAir = context.DeclareInt();
        _ticksInGround = context.DeclareInt();
        _tileX = context.DeclareInt(-1);
        _tileY = context.DeclareInt(-1);
        _tileZ = context.DeclareInt(-1);
    }

    /// <summary>Whether this entity is an arrow — the capability check that replaced `is EntityArrow`.</summary>
    public static bool IsArrow(Entity? entity) => entity?.Behaviors.Find<ArrowBehavior>() is not null;

    /// <summary>Who loosed this arrow, or null when it is not an arrow or nobody owns it.</summary>
    public static EntityLiving? OwnerOf(Entity? entity) =>
        entity?.Behaviors.Find<ArrowBehavior>() is { } arrow ? arrow.Owner(entity) : null;

    /// <summary>
    ///     The bow-and-skeleton spawner: positions the arrow at the shooter's eyes, nudged back so
    ///     it does not clip their face, and looses it the way they are looking. The caller spawns
    ///     the returned entity, and a mob re-aims it with <see cref="SetHeading" /> after.
    /// </summary>
    public static Entity Shoot(IWorldContext world, EntityLiving owner)
    {
        Entity arrow = EntityRegistry.ByName("arrow").Create(world);
        ArrowBehavior flight = arrow.Behaviors.Find<ArrowBehavior>()!;
        arrow.State.SetRef(flight._owner, owner);
        arrow.State[flight._belongsToPlayer] = owner is EntityPlayer;
        arrow.SetPositionAndAnglesKeepPrevAngles(owner.X, owner.Y + owner.EyeHeight, owner.Z, owner.Yaw, owner.Pitch);
        arrow.X -= MathHelper.Cos(arrow.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        arrow.Y -= 0.1F;
        arrow.Z -= MathHelper.Sin(arrow.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        arrow.SetPosition(arrow.X, arrow.Y, arrow.Z);
        arrow.VelocityX = -MathHelper.Sin(arrow.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(arrow.Pitch / 180.0F * (float)Math.PI);
        arrow.VelocityZ = MathHelper.Cos(arrow.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(arrow.Pitch / 180.0F * (float)Math.PI);
        arrow.VelocityY = -MathHelper.Sin(arrow.Pitch / 180.0F * (float)Math.PI);
        flight.SetHeading(arrow, arrow.VelocityX, arrow.VelocityY, arrow.VelocityZ, 1.5F, 1.0F);
        return arrow;
    }

    public EntityLiving? Owner(Entity self) => self.State.GetRef(_owner);

    public void SetOwner(Entity self, EntityLiving owner) => self.State.SetRef(_owner, owner);

    /// <summary>Whether a player may pull this arrow back out of the ground.</summary>
    public bool BelongsToPlayer(Entity self) => self.State[_belongsToPlayer];

    public void SetBelongsToPlayer(Entity self, bool belongs) => self.State[_belongsToPlayer] = belongs;

    /// <summary>Ticks of impact wobble left, for the renderer.</summary>
    public int Shake(Entity self) => self.State[_shake];

    public void SetHeading(Entity self, double x, double y, double z, float speed, float spread)
    {
        float length = MathHelper.Sqrt(x * x + y * y + z * z);
        x /= length;
        y /= length;
        z /= length;
        x += self.Random.NextGaussian() * 0.0075F * spread;
        y += self.Random.NextGaussian() * 0.0075F * spread;
        z += self.Random.NextGaussian() * 0.0075F * spread;
        x *= speed;
        y *= speed;
        z *= speed;
        self.VelocityX = x;
        self.VelocityY = y;
        self.VelocityZ = z;
        float horizontalSpeed = MathHelper.Sqrt(x * x + z * z);
        self.PrevYaw = self.Yaw = (float)(Math.Atan2(x, z) * 180.0D / (float)Math.PI);
        self.PrevPitch = self.Pitch = (float)(Math.Atan2(y, horizontalSpeed) * 180.0D / (float)Math.PI);
        self.State[_ticksInGround] = 0;
    }

    public bool OnVelocityFromServer(Entity self, double vx, double vy, double vz)
    {
        self.VelocityX = vx;
        self.VelocityY = vy;
        self.VelocityZ = vz;
        if (self.PrevPitch != 0.0F || self.PrevYaw != 0.0F) return true;

        float length = MathHelper.Sqrt(vx * vx + vz * vz);
        self.PrevYaw = self.Yaw = (float)(Math.Atan2(vx, vz) * 180.0D / (float)Math.PI);
        self.PrevPitch = self.Pitch = (float)(Math.Atan2(vy, length) * 180.0D / (float)Math.PI);
        self.SetPositionAndAnglesKeepPrevAngles(self.X, self.Y, self.Z, self.Yaw, self.Pitch);
        self.State[_ticksInGround] = 0;
        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();
        if (self.PrevPitch == 0.0F && self.PrevYaw == 0.0F)
        {
            float length = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
            self.PrevYaw = self.Yaw = (float)(Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0D / (float)Math.PI);
            self.PrevPitch = self.Pitch = (float)(Math.Atan2(self.VelocityY, length) * 180.0D / (float)Math.PI);
        }

        int blockId = self.World.Reader.GetBlockId(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
        if (blockId > 0)
        {
            Block.Blocks[blockId].updateBoundingBox(self.World.Reader, self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            Box? box = Block.Blocks[blockId].GetCollisionShape(self.World.Reader, self.World.Entities, self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            if (box != null && box.Value.Contains(new Vec3D(self.X, self.Y, self.Z)))
            {
                self.State[_inGround] = true;
            }
        }

        if (self.State[_shake] > 0)
        {
            --self.State[_shake];
        }

        if (self.State[_inGround])
        {
            blockId = self.World.Reader.GetBlockId(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            int blockMeta = self.World.Reader.GetBlockMeta(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            if (blockId == self.State[_inTile] && blockMeta == self.State[_inData])
            {
                ++self.State[_ticksInGround];
                if (self.State[_ticksInGround] == 1200)
                {
                    self.MarkDead();
                }
            }
            else
            {
                self.State[_inGround] = false;
                self.VelocityX *= self.Random.NextFloat() * 0.2F;
                self.VelocityY *= self.Random.NextFloat() * 0.2F;
                self.VelocityZ *= self.Random.NextFloat() * 0.2F;
                self.State[_ticksInGround] = 0;
                self.State[_ticksInAir] = 0;
            }
        }
        else
        {
            ++self.State[_ticksInAir];
            Vec3D rayStart = new(self.X, self.Y, self.Z);
            Vec3D rayEnd = new(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
            HitResult hit = self.World.Reader.Raycast(rayStart, rayEnd, false, true);
            if (hit.Type != HitResultType.MISS)
            {
                rayEnd = new Vec3D(hit.Pos.x, hit.Pos.y, hit.Pos.z);
            }

            EntityLiving? owner = Owner(self);
            Entity? hitEntity = null;
            List<Entity> candidates = self.World.Entities.GetEntities(self, self.BoundingBox.Stretch(self.VelocityX, self.VelocityY, self.VelocityZ).Expand(1.0D, 1.0D, 1.0D));
            double minHitDistance = 0.0D;

            float expandAmount;
            foreach (Entity entity in candidates)
            {
                if (!entity.HasCollision || (Equals(entity, owner) && self.State[_ticksInAir] < 5)) continue;

                expandAmount = 0.3F;
                Box expandedBox = entity.BoundingBox.Expand(expandAmount, expandAmount, expandAmount);
                HitResult hitResult = expandedBox.Raycast(rayStart, rayEnd);
                if (hitResult.Type == HitResultType.MISS) continue;

                double hitDistance = rayStart.distanceTo(hitResult.Pos);
                if (!(hitDistance < minHitDistance) && minHitDistance != 0.0D) continue;

                hitEntity = entity;
                minHitDistance = hitDistance;
            }

            if (hitEntity != null)
            {
                hit = new HitResult(hitEntity);
            }

            float horizontalSpeed;
            if (hit.Type != HitResultType.MISS)
            {
                if (hit.Entity != null)
                {
                    if (hit.Entity.Damage(owner, _damage))
                    {
                        self.World.Broadcaster.PlaySoundAtEntity(self, "random.drr", 1.0F, 1.2F / (self.Random.NextFloat() * 0.2F + 0.9F));
                        self.MarkDead();
                    }
                    else
                    {
                        self.VelocityX *= -0.1F;
                        self.VelocityY *= -0.1F;
                        self.VelocityZ *= -0.1F;
                        self.Yaw += 180.0F;
                        self.PrevYaw += 180.0F;
                        self.State[_ticksInAir] = 0;
                    }
                }
                else
                {
                    self.State[_tileX] = hit.BlockX;
                    self.State[_tileY] = hit.BlockY;
                    self.State[_tileZ] = hit.BlockZ;
                    self.State[_inTile] = self.World.Reader.GetBlockId(hit.BlockX, hit.BlockY, hit.BlockZ);
                    self.State[_inData] = self.World.Reader.GetBlockMeta(hit.BlockX, hit.BlockY, hit.BlockZ);
                    self.VelocityX = (float)(hit.Pos.x - self.X);
                    self.VelocityY = (float)(hit.Pos.y - self.Y);
                    self.VelocityZ = (float)(hit.Pos.z - self.Z);
                    horizontalSpeed = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityY * self.VelocityY + self.VelocityZ * self.VelocityZ);
                    self.X -= self.VelocityX / horizontalSpeed * 0.05F;
                    self.Y -= self.VelocityY / horizontalSpeed * 0.05F;
                    self.Z -= self.VelocityZ / horizontalSpeed * 0.05F;
                    self.World.Broadcaster.PlaySoundAtEntity(self, "random.drr", 1.0F, 1.2F / (self.Random.NextFloat() * 0.2F + 0.9F));
                    self.State[_inGround] = true;
                    self.State[_shake] = 7;
                }
            }

            self.X += self.VelocityX;
            self.Y += self.VelocityY;
            self.Z += self.VelocityZ;
            horizontalSpeed = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
            self.Yaw = (float)(Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0D / (float)Math.PI);

            self.Pitch = (float)(Math.Atan2(self.VelocityY, horizontalSpeed) * 180.0D / Math.PI);
            while (self.Pitch - self.PrevPitch < -180.0F) self.PrevPitch -= 360.0F;
            while (self.Pitch - self.PrevPitch >= 180.0F) self.PrevPitch += 360.0F;
            while (self.Yaw - self.PrevYaw < -180.0F) self.PrevYaw -= 360.0F;
            while (self.Yaw - self.PrevYaw >= 180.0F) self.PrevYaw += 360.0F;

            self.Pitch = self.PrevPitch + (self.Pitch - self.PrevPitch) * 0.2F;
            self.Yaw = self.PrevYaw + (self.Yaw - self.PrevYaw) * 0.2F;
            float drag = 0.99F;
            const float gravity = 0.03F;

            if (self.IsInWater)
            {
                for (int i = 0; i < 4; ++i)
                {
                    const float bubbleOffset = 0.25F;
                    self.World.Broadcaster.AddParticle("bubble", self.X - self.VelocityX * bubbleOffset, self.Y - self.VelocityY * bubbleOffset, self.Z - self.VelocityZ * bubbleOffset, self.VelocityX, self.VelocityY, self.VelocityZ);
                }

                drag = 0.8F;
            }

            self.VelocityX *= drag;
            self.VelocityY *= drag;
            self.VelocityZ *= drag;
            self.VelocityY -= gravity;
            self.SetPosition(self.X, self.Y, self.Z);
        }

        return true;
    }

    /// <summary>A player-owned arrow stuck in the ground, done shaking, can be pulled back out.</summary>
    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        if (self.World.IsRemote) return;
        if (!self.State[_inGround] || !self.State[_belongsToPlayer] || self.State[_shake] > 0 || !player.Inventory.AddItemStackToInventory(new ItemStack(Item.ByName("arrow"), 1))) return;

        self.World.Broadcaster.PlaySoundAtEntity(self, "random.pop", 0.2F, ((self.Random.NextFloat() - self.Random.NextFloat()) * 0.7F + 1.0F) * 2.0F);
        player.sendPickup(self, 1);
        self.MarkDead();
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetShort("xTile", (short)self.State[_tileX]);
        nbt.SetShort("yTile", (short)self.State[_tileY]);
        nbt.SetShort("zTile", (short)self.State[_tileZ]);
        nbt.SetByte("inTile", (sbyte)self.State[_inTile]);
        nbt.SetByte("inData", (sbyte)self.State[_inData]);
        nbt.SetByte("shake", (sbyte)self.State[_shake]);
        nbt.SetByte("inGround", (sbyte)(self.State[_inGround] ? 1 : 0));
        nbt.SetBoolean("player", self.State[_belongsToPlayer]);
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_tileX] = nbt.GetShort("xTile");
        self.State[_tileY] = nbt.GetShort("yTile");
        self.State[_tileZ] = nbt.GetShort("zTile");
        self.State[_inTile] = nbt.GetByte("inTile") & 255;
        self.State[_inData] = nbt.GetByte("inData") & 255;
        self.State[_shake] = nbt.GetByte("shake") & 255;
        self.State[_inGround] = nbt.GetByte("inGround") == 1;
        self.State[_belongsToPlayer] = nbt.GetBoolean("player");
    }
}
