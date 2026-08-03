using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util.Hit;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A small thrown projectile: flies on a shallow arc, remembers who threw it so it cannot hit
///     them point-blank, and pops on the first block or entity it touches. The snowball and the egg
///     share this flight exactly; the egg adds a hatch roll on impact, declared as data.
///     <para>
///         Which mob threw it is per-instance state set by <see cref="Throw" />, the spawner for a
///         hand-thrown projectile. A dispenser creates the entity bare and aims it with
///         <see cref="SetHeading" />.
///     </para>
/// </summary>
public sealed class ThrownProjectileBehavior : IEntityTicker, IEntityPersistence, IEntityInteractable, IEntityPhysics
{
    /// <summary>
    ///     The egg's hatch roll, absent for the snowball: on impact, a 1-in-<c>Chance</c>
    ///     chance of one hatchling, itself 1-in-<c>BonusChance</c> upgraded to <c>BonusCount</c>.
    /// </summary>
    private readonly (string Entity, int Chance, int BonusChance, int BonusCount)? _hatch;

    private readonly string _impactParticle;
    private readonly StateHandle<bool> _inGround;
    private readonly StateHandle<int> _inTile;
    private readonly StateHandle<int> _shake;
    private readonly StateHandle<EntityLiving> _thrower;
    private readonly StateHandle<int> _ticksInAir;
    private readonly StateHandle<int> _ticksInGround;
    private readonly StateHandle<int> _tileX;
    private readonly StateHandle<int> _tileY;
    private readonly StateHandle<int> _tileZ;

    public ThrownProjectileBehavior(
        EntityStateLayout layout,
        string impactParticle,
        (string Entity, int Chance, int BonusChance, int BonusCount)? hatch)
    {
        _impactParticle = impactParticle;
        _hatch = hatch;

        _thrower = layout.DeclareRef<EntityLiving>();
        _inGround = layout.DeclareBool();
        _inTile = layout.DeclareInt();
        _shake = layout.DeclareInt();
        _ticksInAir = layout.DeclareInt();
        _ticksInGround = layout.DeclareInt();
        _tileX = layout.DeclareInt(-1);
        _tileY = layout.DeclareInt(-1);
        _tileZ = layout.DeclareInt(-1);
    }

    /// <summary>
    ///     A verbatim Beta quirk, from copying the arrow's pickup code: a projectile stuck in the
    ///     ground hands its thrower an arrow. Unreachable in practice, since flight never sets the
    ///     in-ground flag and the thrower does not survive a save, but ported as found.
    /// </summary>
    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        if (!self.State[_inGround] || !Equals(Thrower(self), player) || self.State[_shake] > 0 || !player.Inventory.AddItemStackToInventory(new ItemStack(Item.ByName("arrow"), 1)))
        {
            return;
        }

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
        nbt.SetByte("shake", (sbyte)self.State[_shake]);
        nbt.SetByte("inGround", (sbyte)(self.State[_inGround] ? 1 : 0));
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_tileX] = nbt.GetShort("xTile");
        self.State[_tileY] = nbt.GetShort("yTile");
        self.State[_tileZ] = nbt.GetShort("zTile");
        self.State[_inTile] = nbt.GetByte("inTile") & 255;
        self.State[_shake] = nbt.GetByte("shake") & 255;
        self.State[_inGround] = nbt.GetByte("inGround") == 1;
    }

    public bool OnVelocityFromServer(Entity self, double vx, double vy, double vz)
    {
        self.VelocityX = vx;
        self.VelocityY = vy;
        self.VelocityZ = vz;
        if (self.PrevPitch != 0.0F || self.PrevYaw != 0.0F)
        {
            return true;
        }

        float horizontalLength = MathHelper.Sqrt(vx * vx + vz * vz);
        self.PrevYaw = self.Yaw = (float)(Math.Atan2(vx, vz) * 180.0D / (float)Math.PI);
        self.PrevPitch = self.Pitch = (float)(Math.Atan2(vy, horizontalLength) * 180.0D / (float)Math.PI);
        return true;
    }

    public bool OnTickEntity(Entity self)
    {
        self.LastTickX = self.X;
        self.LastTickY = self.Y;
        self.LastTickZ = self.Z;
        self.BaseTick();
        if (self.State[_shake] > 0)
        {
            --self.State[_shake];
        }

        if (self.State[_inGround])
        {
            int blockId = self.World.Reader.GetBlockId(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            if (blockId == self.State[_inTile])
            {
                ++self.State[_ticksInGround];
                if (self.State[_ticksInGround] == 1200)
                {
                    self.MarkDead();
                }

                return true;
            }

            self.State[_inGround] = false;
            self.VelocityX *= self.Random.NextFloat() * 0.2F;
            self.VelocityY *= self.Random.NextFloat() * 0.2F;
            self.VelocityZ *= self.Random.NextFloat() * 0.2F;
            self.State[_ticksInGround] = 0;
            self.State[_ticksInAir] = 0;
        }
        else
        {
            ++self.State[_ticksInAir];
        }

        Vec3D rayStart = new(self.X, self.Y, self.Z);
        Vec3D rayEnd = new(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        HitResult hit = self.World.Reader.Raycast(rayStart, rayEnd);
        rayStart = new Vec3D(self.X, self.Y, self.Z);
        rayEnd = new Vec3D(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        if (hit.Type != HitResultType.Miss)
        {
            rayEnd = new Vec3D(hit.Pos.x, hit.Pos.y, hit.Pos.z);
        }

        if (!self.World.IsRemote)
        {
            EntityLiving? thrower = Thrower(self);
            Entity? hitEntity = null;
            List<Entity> entities = self.World.Entities.GetEntities(self, self.BoundingBox.Stretch(self.VelocityX, self.VelocityY, self.VelocityZ).Expand(1.0D, 1.0D, 1.0D));
            double minHitDistance = 0.0D;

            foreach (Entity entity in entities)
            {
                if (!entity.HasCollision || (Equals(entity, thrower) && self.State[_ticksInAir] < 5))
                {
                    continue;
                }

                const float expandAmount = 0.3F;
                Box expandedBox = entity.BoundingBox.Expand(expandAmount, expandAmount, expandAmount);
                HitResult entityHit = expandedBox.Raycast(rayStart, rayEnd);
                if (entityHit.Type == HitResultType.Miss)
                {
                    continue;
                }

                double distance = rayStart.distanceTo(entityHit.Pos);
                if (!(distance < minHitDistance) && minHitDistance != 0.0D)
                {
                    continue;
                }

                hitEntity = entity;
                minHitDistance = distance;
            }

            if (hitEntity != null)
            {
                hit = new HitResult(hitEntity);
            }
        }

        if (hit.Type != HitResultType.Miss)
        {
            OnImpact(self, hit);
            self.MarkDead();
        }

        self.X += self.VelocityX;
        self.Y += self.VelocityY;
        self.Z += self.VelocityZ;
        float horizontalSpeed = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        self.Yaw = (float)(Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0D / Math.PI);
        self.Pitch = (float)(Math.Atan2(self.VelocityY, horizontalSpeed) * 180.0D / Math.PI);

        while (self.Pitch - self.PrevPitch < -180.0F)
        {
            self.PrevPitch -= 360.0F;
        }

        while (self.Pitch - self.PrevPitch >= 180.0F)
        {
            self.PrevPitch += 360.0F;
        }

        while (self.Yaw - self.PrevYaw < -180.0F)
        {
            self.PrevYaw -= 360.0F;
        }

        while (self.Yaw - self.PrevYaw >= 180.0F)
        {
            self.PrevYaw += 360.0F;
        }

        self.Pitch = self.PrevPitch + (self.Pitch - self.PrevPitch) * 0.2F;
        self.Yaw = self.PrevYaw + (self.Yaw - self.PrevYaw) * 0.2F;
        float drag = 0.99F;
        const float gravity = 0.03F;
        if (self.IsInWater)
        {
            for (int i = 0; i < 4; ++i)
            {
                const float trailOffset = 0.25F;
                self.World.Broadcaster.AddParticle("bubble", self.X - self.VelocityX * trailOffset, self.Y - self.VelocityY * trailOffset, self.Z - self.VelocityZ * trailOffset, self.VelocityX, self.VelocityY, self.VelocityZ);
            }

            drag = 0.8F;
        }

        self.VelocityX *= drag;
        self.VelocityY *= drag;
        self.VelocityZ *= drag;
        self.VelocityY -= gravity;
        self.SetPosition(self.X, self.Y, self.Z);
        return true;
    }

    /// <summary>
    ///     Positions the projectile at the thrower's eyes, nudged back so it does not clip their
    ///     face, and launches it the way they are looking. The caller spawns the returned entity.
    /// </summary>
    public static Entity Throw(IWorldContext world, string typeName, EntityLiving thrower)
    {
        Entity projectile = EntityRegistry.ByName(typeName).Create(world);
        ThrownProjectileBehavior thrown = projectile.Behaviors.Find<ThrownProjectileBehavior>()!;
        projectile.State.SetRef(thrown._thrower, thrower);
        projectile.SetPositionAndAnglesKeepPrevAngles(thrower.X, thrower.Y + thrower.EyeHeight, thrower.Z, thrower.Yaw, thrower.Pitch);
        projectile.X -= MathHelper.Cos(projectile.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        projectile.Y -= 0.1F;
        projectile.Z -= MathHelper.Sin(projectile.Yaw / 180.0F * (float)Math.PI) * 0.16F;
        projectile.SetPosition(projectile.X, projectile.Y, projectile.Z);
        const float speed = 0.4F;
        projectile.VelocityX = -MathHelper.Sin(projectile.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(projectile.Pitch / 180.0F * (float)Math.PI) * speed;
        projectile.VelocityZ = MathHelper.Cos(projectile.Yaw / 180.0F * (float)Math.PI) * MathHelper.Cos(projectile.Pitch / 180.0F * (float)Math.PI) * speed;
        projectile.VelocityY = -MathHelper.Sin(projectile.Pitch / 180.0F * (float)Math.PI) * speed;
        thrown.SetHeading(projectile, projectile.VelocityX, projectile.VelocityY, projectile.VelocityZ, 1.5F, 1.0F);
        return projectile;
    }

    public EntityLiving? Thrower(Entity self) => self.State.GetRef(_thrower);

    public void SetHeading(Entity self, double dirX, double dirY, double dirZ, float speed, float spread)
    {
        float length = MathHelper.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        dirX /= length;
        dirY /= length;
        dirZ /= length;
        dirX += self.Random.NextGaussian() * 0.0075F * spread;
        dirY += self.Random.NextGaussian() * 0.0075F * spread;
        dirZ += self.Random.NextGaussian() * 0.0075F * spread;
        dirX *= speed;
        dirY *= speed;
        dirZ *= speed;
        self.VelocityX = dirX;
        self.VelocityY = dirY;
        self.VelocityZ = dirZ;
        float horizontalLength = MathHelper.Sqrt(dirX * dirX + dirZ * dirZ);
        self.PrevYaw = self.Yaw = (float)(Math.Atan2(dirX, dirZ) * 180.0D / (float)Math.PI);
        self.PrevPitch = self.Pitch = (float)(Math.Atan2(dirY, horizontalLength) * 180.0D / (float)Math.PI);
        self.State[_ticksInGround] = 0;
    }

    private void OnImpact(Entity self, HitResult hit)
    {
        if (hit.Entity != null && hit.Entity.Damage(Thrower(self), 0))
        {
        }

        if (_hatch is { } hatch && !self.World.IsRemote && self.Random.NextInt(hatch.Chance) == 0)
        {
            int hatchlings = self.Random.NextInt(hatch.BonusChance) == 0 ? hatch.BonusCount : 1;
            for (int i = 0; i < hatchlings; ++i)
            {
                Entity hatchling = EntityRegistry.ByName(hatch.Entity).Create(self.World);
                hatchling.SetPositionAndAnglesKeepPrevAngles(self.X, self.Y, self.Z, self.Yaw, 0.0F);
                self.World.SpawnEntity(hatchling);
            }
        }

        for (int i = 0; i < 8; ++i)
        {
            self.World.Broadcaster.AddParticle(_impactParticle, self.X, self.Y, self.Z, 0.0D, 0.0D, 0.0D);
        }
    }
}
