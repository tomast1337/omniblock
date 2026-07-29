using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     What it means to be a monster: it spawns in darkness, paths towards darkness, ages towards
///     despawn twice as fast in daylight, turns on whoever hurts it, and is removed outright when
///     the world is set to peaceful.
///     <para>
///         Three slots from one entry, because all of it is the same idea. A mob that declares its
///         own Physics behavior beside this one replaces the spawn and path rules while keeping the
///         rest, which is how a zombie pigman spawns in the lit Nether.
///     </para>
/// </summary>
public sealed class HostileMonsterBehavior : IEntityPhysics, IEntityTicker, IEntityLifecycle
{
    /// <summary>
    ///     Turns on the player who drew blood. A rider, a mount, and the monster itself are all
    ///     spared, so a skeleton is not provoked by the spider carrying it.
    /// </summary>
    public void OnDamageApplied(EntityLiving self, Entity? attacker, int amount)
    {
        if (self is not EntityCreature creature
            || Equals(self.Passenger, attacker)
            || Equals(self.Vehicle, attacker)
            || Equals(attacker, self))
        {
            return;
        }

        if (attacker is EntityPlayer { GameMode.CanBeTargeted: true })
        {
            creature.Target = attacker;
        }
    }

    public float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) =>
        0.5F - self.World.Lighting.GetLuminance(x, y, z);

    /// <summary>
    ///     Dark enough for a monster, where a thunderstorm counts as dark whatever the hour.
    /// </summary>
    public bool? CanSpawn(EntityLiving self)
    {
        int x = MathHelper.Floor(self.X);
        int y = MathHelper.Floor(self.BoundingBox.MinY);
        int z = MathHelper.Floor(self.Z);

        if (self.World.Lighting.GetBrightness(LightType.Sky, x, y, z) > self.Random.NextInt(32))
        {
            return false;
        }

        return LightLevelAt(self, x, y, z) <= self.Random.NextInt(8)
               && self.World.Entities.CanSpawnEntity(self.BoundingBox)
               && self.World.Entities.GetEntityCollisionsScratch(self, self.BoundingBox).Count == 0
               && !self.World.Reader.IsMaterialInBox(self.BoundingBox, m => m.IsFluid)
               && GetBlockPathWeight(self, x, y, z) >= 0.0F;
    }

    /// <summary>Daylight ages a monster towards despawn at twice the usual rate.</summary>
    public void OnTickMovement(EntityLiving self)
    {
        if (self.GetBrightnessAtEyes(1.0F) > 0.5F)
        {
            self.EntityAge += 2;
        }
    }

    /// <summary>Peaceful leaves no monsters standing.</summary>
    public bool OnTickLiving(EntityLiving self)
    {
        if (self.World is { IsRemote: false, Difficulty: 0 })
        {
            self.MarkDead();
        }

        // Adds to the AI instead of replacing it.
        return false;
    }

    /// <summary>Whether this entity is a monster.</summary>
    public static bool IsMonster(Entity? entity) => entity?.Behaviors.Find<HostileMonsterBehavior>() is not null;

    /// <summary>
    ///     A thunderstorm is dark enough to spawn under: ambient darkness is forced to its
    ///     night-time value for the reading.
    /// </summary>
    private static int LightLevelAt(EntityLiving self, int x, int y, int z)
    {
        if (!self.World.Environment.IsThundering())
        {
            return self.World.Lighting.GetLightLevel(x, y, z);
        }

        int ambientDarkness = self.World.Environment.AmbientDarkness;
        self.World.Environment.AmbientDarkness = 10;
        int lightLevel = self.World.Lighting.GetLightLevel(x, y, z);
        self.World.Environment.AmbientDarkness = ambientDarkness;

        return lightLevel;
    }
}
