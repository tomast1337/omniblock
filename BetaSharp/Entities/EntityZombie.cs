using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityZombie : EntityMonster
{
    public EntityZombie(IWorldContext world) : this(world, EntityRegistry.ByName("zombie"))
    {
    }

    protected EntityZombie(IWorldContext world, EntityType type) : base(world, type)
    {
    }


}
