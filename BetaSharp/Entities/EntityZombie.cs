using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityZombie : EntityMonster
{
    public EntityZombie(IWorldContext world) : this(world, EntityRegistry.ByName("zombie").RequireDefinition())
    {
    }

    protected EntityZombie(IWorldContext world, EntityDefinition definition) : base(world, definition)
    {
    }

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
