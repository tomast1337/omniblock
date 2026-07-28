using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntitySkeleton : EntityMonster
{
    private static readonly ItemStack s_defaultHeldItem = new(Item.ByName("bow"), 1);

    public EntitySkeleton(IWorldContext world) : base(world, EntityRegistry.ByName("skeleton").RequireDefinition())
    {

        // Two pools, so arrows and bones roll independently and both can drop.
    }
    public override ItemStack HeldItem => s_defaultHeldItem;


}
