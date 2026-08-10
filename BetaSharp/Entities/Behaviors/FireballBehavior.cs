using OmniBlock.Entities.State;
using OmniBlock.NBT;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A ghast's fireball. No gravity: it rides a constant acceleration vector ("power") picked when
///     shot, burns as it flies, trails smoke, and explodes on whatever it touches first. A punch
///     deflects it, re-aiming velocity and power along the attacker's look vector.
///     <para>
///         Who shot it and its power vector are per-instance state. <see cref="Shoot" /> is the
///         server-side spawner; <see cref="SetDirection" /> re-derives power on the client from the
///         spawn packet. Neither survives a save, matching Beta.
///     </para>
/// </summary>
public sealed class FireballBehavior : IEntityTicker, IEntityLifecycle, IEntityPersistence
{
    private readonly StateHandle<int> _blockId;

    private readonly float _explosionPower;
    private readonly StateHandle<int> _inAirTime;
    private readonly StateHandle<bool> _inGround;
    private readonly StateHandle<EntityLiving> _owner;
    private readonly StateHandle<double> _powerX;
    private readonly StateHandle<double> _powerY;
    private readonly StateHandle<double> _powerZ;
    private readonly StateHandle<int> _removalTimer;
    private readonly StateHandle<int> _shake;
    private readonly StateHandle<int> _tileX;
    private readonly StateHandle<int> _tileY;
    private readonly StateHandle<int> _tileZ;

    public FireballBehavior(EntityStateLayout layout, float explosionPower)
    {
        _explosionPower = explosionPower;

        _owner = layout.DeclareRef<EntityLiving>();
        _inGround = layout.DeclareBool();
        _blockId = layout.DeclareInt();
        _shake = layout.DeclareInt();
        _inAirTime = layout.DeclareInt();
        _removalTimer = layout.DeclareInt();
        _tileX = layout.DeclareInt(-1);
        _tileY = layout.DeclareInt(-1);
        _tileZ = layout.DeclareInt(-1);
        _powerX = layout.DeclareDouble();
        _powerY = layout.DeclareDouble();
        _powerZ = layout.DeclareDouble();
    }

    /// <summary>
    ///     A punch deflects instead of damaging: velocity and power re-aim along the attacker's look
    ///     vector, sending the fireball back the way the punch was facing.
    /// </summary>
    public bool? Damage(Entity self, Entity? attacker, int amount)
    {
        self.VelocityModified = true;
        if (attacker == null)
        {
            return false;
        }

        Vec3D? lookVector = attacker.LookVector;
        if (lookVector == null)
        {
            return true;
        }

        self.VelocityX = lookVector.Value.X;
        self.VelocityY = lookVector.Value.Y;
        self.VelocityZ = lookVector.Value.Z;

        self.State[_powerX] = self.VelocityX * 0.1D;
        self.State[_powerY] = self.VelocityY * 0.1D;
        self.State[_powerZ] = self.VelocityZ * 0.1D;

        return true;
    }

    public void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
        nbt.SetShort("xTile", (short)self.State[_tileX]);
        nbt.SetShort("yTile", (short)self.State[_tileY]);
        nbt.SetShort("zTile", (short)self.State[_tileZ]);
        nbt.SetByte("inTile", (sbyte)self.State[_blockId]);
        nbt.SetByte("shake", (sbyte)self.State[_shake]);
        nbt.SetByte("inGround", (sbyte)(self.State[_inGround] ? 1 : 0));
    }

    public void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
        self.State[_tileX] = nbt.GetShort("xTile");
        self.State[_tileY] = nbt.GetShort("yTile");
        self.State[_tileZ] = nbt.GetShort("zTile");
        self.State[_blockId] = nbt.GetByte("inTile") & 255;
        self.State[_shake] = nbt.GetByte("shake") & 255;
        self.State[_inGround] = nbt.GetByte("inGround") == 1;
    }

    public bool OnTickEntity(Entity self)
    {
        self.BaseTick();
        self.FireTicks = 10;
        if (self.State[_shake] > 0)
        {
            --self.State[_shake];
        }

        if (self.State[_inGround])
        {
            int inGroundBlockId = self.World.Reader.GetBlockId(self.State[_tileX], self.State[_tileY], self.State[_tileZ]);
            if (inGroundBlockId == self.State[_blockId])
            {
                ++self.State[_removalTimer];
                if (self.State[_removalTimer] == 1200)
                {
                    self.MarkDead();
                }

                return true;
            }

            self.State[_inGround] = false;
            self.VelocityX *= self.Random.NextFloat() * 0.2F;
            self.VelocityY *= self.Random.NextFloat() * 0.2F;
            self.VelocityZ *= self.Random.NextFloat() * 0.2F;
            self.State[_removalTimer] = 0;
            self.State[_inAirTime] = 0;
        }
        else
        {
            ++self.State[_inAirTime];
        }

        Vec3D startPos = new(self.X, self.Y, self.Z);
        Vec3D endPos = new(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        HitResult hitResult = self.World.Reader.Raycast(startPos, endPos);
        startPos = new Vec3D(self.X, self.Y, self.Z);
        endPos = new Vec3D(self.X + self.VelocityX, self.Y + self.VelocityY, self.Z + self.VelocityZ);
        if (hitResult.Type != HitResultType.Miss)
        {
            endPos = new Vec3D(hitResult.Pos.X, hitResult.Pos.Y, hitResult.Pos.Z);
        }

        EntityLiving? owner = Owner(self);
        Entity? hitEntity = null;
        List<Entity> candidateEntities = self.World.Entities.GetEntities(self, self.BoundingBox.Stretch(self.VelocityX, self.VelocityY, self.VelocityZ).Expand(1.0D, 1.0D, 1.0D));
        double nearestHitDistance = 0.0D;

        foreach (Entity candidateEntity in candidateEntities)
        {
            if (!candidateEntity.HasCollision || (Equals(candidateEntity, owner) && self.State[_inAirTime] < 25))
            {
                continue;
            }

            const float collisionMargin = 0.3F;
            Box candidateBox = candidateEntity.BoundingBox.Expand(collisionMargin, collisionMargin, collisionMargin);
            HitResult candidateHit = candidateBox.Raycast(startPos, endPos);
            if (candidateHit.Type == HitResultType.Miss)
            {
                continue;
            }

            double hitDistance = startPos.DistanceTo(candidateHit.Pos);
            if (!(hitDistance < nearestHitDistance) && nearestHitDistance != 0.0D)
            {
                continue;
            }

            hitEntity = candidateEntity;
            nearestHitDistance = hitDistance;
        }

        if (hitEntity != null)
        {
            hitResult = new HitResult(hitEntity);
        }

        if (hitResult.Type != HitResultType.Miss)
        {
            if (!self.World.IsRemote)
            {
                if (hitResult.Entity != null && hitResult.Entity.Damage(owner, 0))
                {
                }

                self.World.CreateExplosion(null, self.X, self.Y, self.Z, _explosionPower, true);
            }

            self.MarkDead();
        }

        self.X += self.VelocityX;
        self.Y += self.VelocityY;
        self.Z += self.VelocityZ;
        float horizontalSpeed = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        self.Yaw = (float)(Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0D / (float)Math.PI);

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
        float drag = 0.95F;
        if (self.IsInWater)
        {
            for (int bubbleIndex = 0; bubbleIndex < 4; ++bubbleIndex)
            {
                const float bubbleOffset = 0.25F;
                self.World.Broadcaster.AddParticle("bubble", self.X - self.VelocityX * bubbleOffset, self.Y - self.VelocityY * bubbleOffset, self.Z - self.VelocityZ * bubbleOffset, self.VelocityX, self.VelocityY, self.VelocityZ);
            }

            drag = 0.8F;
        }

        self.VelocityX += self.State[_powerX];
        self.VelocityY += self.State[_powerY];
        self.VelocityZ += self.State[_powerZ];
        self.VelocityX *= drag;
        self.VelocityY *= drag;
        self.VelocityZ *= drag;
        self.World.Broadcaster.AddParticle("smoke", self.X, self.Y + 0.5D, self.Z, 0.0D, 0.0D, 0.0D);
        self.SetPosition(self.X, self.Y, self.Z);
        return true;
    }

    /// <summary>
    ///     Places the fireball on the shooter and aims its power at the target offset, wobbled by
    ///     the shooter's aim spread. The caller may reposition it before spawning; the ghast holds it
    ///     out in front of its face.
    /// </summary>
    public static Entity Shoot(IWorldContext world, EntityLiving owner, double dx, double dy, double dz)
    {
        Entity fireball = EntityRegistry.ByName("fireball").Create(world);
        FireballBehavior flight = fireball.Behaviors.Find<FireballBehavior>()!;
        fireball.State.SetRef(flight._owner, owner);
        fireball.SetPositionAndAnglesKeepPrevAngles(owner.X, owner.Y, owner.Z, owner.Yaw, owner.Pitch);
        fireball.SetPosition(fireball.X, fireball.Y, fireball.Z);
        fireball.VelocityX = fireball.VelocityY = fireball.VelocityZ = 0.0D;
        dx += fireball.Random.NextGaussian() * 0.4D;
        dy += fireball.Random.NextGaussian() * 0.4D;
        dz += fireball.Random.NextGaussian() * 0.4D;
        flight.SetDirection(fireball, dx, dy, dz);
        return fireball;
    }

    public EntityLiving? Owner(Entity self) => self.State.GetRef(_owner);

    public double PowerX(Entity self) => self.State[_powerX];
    public double PowerY(Entity self) => self.State[_powerY];
    public double PowerZ(Entity self) => self.State[_powerZ];

    /// <summary>Normalises a direction into the fixed-magnitude power vector the flight rides.</summary>
    public void SetDirection(Entity self, double dx, double dy, double dz)
    {
        double length = MathHelper.Sqrt(dx * dx + dy * dy + dz * dz);
        self.State[_powerX] = dx / length * 0.1D;
        self.State[_powerY] = dy / length * 0.1D;
        self.State[_powerZ] = dz / length * 0.1D;
    }
}
