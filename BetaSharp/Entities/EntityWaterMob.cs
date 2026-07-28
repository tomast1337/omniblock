using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public abstract class EntityWaterMob(IWorldContext world, EntityType? type = null) : EntityCreature(world, type)
{
    protected override bool canBreatheUnderwater() => true;

    protected override bool CanSpawnHere() => World.Entities.CanSpawnEntity(BoundingBox);

    protected override int TalkInterval => 120;
}
