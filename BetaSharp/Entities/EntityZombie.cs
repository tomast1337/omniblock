using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityZombie : EntityMonster
{
    private static readonly Item s_feather = Item.ByName("feather");
    public EntityZombie(IWorldContext world) : this(world, MobDefinitions.Zombie)
    {
    }

    protected EntityZombie(IWorldContext world, EntityDefinition definition) : base(world, definition)
    {
        Loot = new LootTableBehavior(LootTable.Single(s_feather, 0, 2));
    }

    public override EntityType Type => EntityRegistry.Zombie;

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
