using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityGiantZombie : EntityMonster
{
    public EntityGiantZombie(IWorldContext world) : base(world, EntityRegistry.ByName("giant"))
    {
        // Scaled rather than authored: the resulting 3.6000001 x 10.799999 box is an artifact of
        // this multiplication, so keeping it in code preserves the exact float values.
        StandingEyeHeight *= 6.0F;
        SetBoundingBoxSpacing(Width * 6.0F, Height * 6.0F);
    }

    protected override float GetBlockPathWeight(int x, int y, int z) => World.Lighting.GetLuminance(x, y, z) - 0.5F;
}
