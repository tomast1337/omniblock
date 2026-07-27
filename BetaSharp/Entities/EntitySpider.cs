using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntitySpider : EntityMonster
{
    private static readonly Item s_string = Item.ByName("string");
    private const double ViewDistance = 16.0D;

    public EntitySpider(IWorldContext world) : base(world)
    {
        Texture = "/mob/spider.png";
        SetBoundingBoxSpacing(1.4F, 0.9F);
        MovementSpeed = 0.8F;
        Attack = new JumpAttackBehavior(2.0F, 6.0F, 10, new MeleeAttackBehavior());
        Targeting = new DarknessOnlyTargetBehavior(ViewDistance);
        Loot = new LootTableBehavior(LootTable.Single(s_string, 0, 2));
    }

    public override EntityType Type => EntityRegistry.Spider;

    protected override double PassengerRidingHeight => Height * 0.75D - 0.5D;

    protected override string? LivingSound => "mob.spider";

    protected override string? HurtSound => "mob.spider";

    protected override string? DeathSound => "mob.spiderdeath";

    protected override bool IsOnLadder => HorizontalCollision;

    protected sealed override void SetBoundingBoxSpacing(float widthOffset, float heightOffset) => base.SetBoundingBoxSpacing(widthOffset, heightOffset);

    public override void PostSpawn()
    {
        if (World.Random.NextInt(100) != 0) return;

        EntitySkeleton skeleton = new(World);
        skeleton.SetPositionAndAnglesKeepPrevAngles(X, Y, Z, Yaw, 0.0F);
        World.SpawnEntity(skeleton);
        skeleton.SetVehicle(this);
    }

    protected override bool BypassesSteppingEffects() => false;

    /// <summary>
    ///     Spiders lose interest when caught in daylight; otherwise the composed jump/melee behavior
    ///     runs unchanged.
    /// </summary>
    protected override void attackEntity(Entity entity, float distance)
    {
        float brightness = GetBrightnessAtEyes(1.0F);
        if (brightness > 0.5F && Random.NextInt(100) == 0)
        {
            Target = null;
            return;
        }

        base.attackEntity(entity, distance);
    }
}
