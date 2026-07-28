using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCow : EntityAnimal
{
    public EntityCow(IWorldContext world) : base(world, EntityRegistry.ByName("cow").RequireDefinition())
    {
    }

}
