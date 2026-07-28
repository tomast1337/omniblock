using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityMonster : EntityCreature
{
    public EntityMonster(IWorldContext world, EntityType? type = null) : base(world, type)
    {
    }

    protected override void TickMovement()
    {
        float brightness = GetBrightnessAtEyes(1.0F);
        if (brightness > 0.5F)
        {
            EntityAge += 2;
        }

        base.TickMovement();
    }

    public override void Tick()
    {
        base.Tick();
        if (World is { IsRemote: false, Difficulty: 0 })
        {
            MarkDead();
        }
    }

    public override bool Damage(Entity? entity, int amount)
    {
        if (!base.Damage(entity, amount)) return false;
        if (Equals(Passenger, entity) || Equals(Vehicle, entity)) return true;
        if (Equals(entity, this)) return true;
        if (entity is EntityPlayer { GameMode.CanBeTargeted: true }) Target = entity;
        return true;
    }

    protected override float GetBlockPathWeight(int x, int y, int z) =>
        Physics?.GetBlockPathWeight(this, x, y, z) ?? 0.5F - World.Lighting.GetLuminance(x, y, z);

    /// <summary>
    ///     The darkness rule. A mob whose Physics slot declares its own placement never reaches
    ///     here — <see cref="EntityLiving.CanSpawn" /> replaces this outright rather than adding to
    ///     it, which is what lets a zombie pigman spawn in the lit Nether.
    /// </summary>
    protected override bool CanSpawnHere()
    {
        int x = MathHelper.Floor(X);
        int y = MathHelper.Floor(BoundingBox.MinY);
        int z = MathHelper.Floor(Z);
        if (World.Lighting.GetBrightness(LightType.Sky, x, y, z) > Random.NextInt(32)) return false;

        int lightLevel = World.Lighting.GetLightLevel(x, y, z);
        if (!World.Environment.IsThundering()) return lightLevel <= Random.NextInt(8) && base.CanSpawnHere();

        int ambientDarkness = World.Environment.AmbientDarkness;
        World.Environment.AmbientDarkness = 10;
        lightLevel = World.Lighting.GetLightLevel(x, y, z);
        World.Environment.AmbientDarkness = ambientDarkness;

        return lightLevel <= Random.NextInt(8) && base.CanSpawnHere();
    }
}
