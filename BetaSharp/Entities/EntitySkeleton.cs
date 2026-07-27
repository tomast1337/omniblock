using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntitySkeleton : EntityMonster
{
    private static readonly EntityType s_type = EntityRegistry.ByName("skeleton");

    private static readonly ItemStack s_defaultHeldItem = new(Item.ByName("bow"), 1);

    public EntitySkeleton(IWorldContext world) : base(world, s_type.RequireDefinition())
    {

        // Two pools, so arrows and bones roll independently and both can drop.
    }
    public override EntityType Type => s_type;

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
