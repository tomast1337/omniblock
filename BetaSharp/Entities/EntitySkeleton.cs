using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntitySkeleton : EntityMonster
{
    private static readonly ItemStack s_defaultHeldItem = new(Item.ByName("bow"), 1);
    private static readonly Item s_arrow = Item.ByName("arrow");
    private static readonly Item s_bone = Item.ByName("bone");

    public EntitySkeleton(IWorldContext world) : base(world, EntityRegistry.Skeleton.RequireDefinition())
    {
        Attack = new RangedAttackBehavior();

        // Two pools, so arrows and bones roll independently and both can drop.
        Loot = new LootTableBehavior(new LootTable(
            new LootPool([LootEntry.Of(s_arrow)], 0, 2),
            new LootPool([LootEntry.Of(s_bone)], 0, 2)));
    }
    public override EntityType Type => EntityRegistry.Skeleton;

    public override ItemStack HeldItem => s_defaultHeldItem;

    protected override void TickMovement()
    {
        if (World.Environment.CanMonsterSpawn())
        {
            float brightness = GetBrightnessAtEyes(1.0F);
            if (brightness > 0.5F && World.Lighting.HasSkyLight(MathHelper.Floor(X), MathHelper.Floor(Y), MathHelper.Floor(Z)) && Random.NextFloat() * 30.0F < (brightness - 0.4F) * 2.0F)
            {
                FireTicks = 300;
            }
        }

        base.TickMovement();
    }

}
