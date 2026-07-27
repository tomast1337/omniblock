namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Splits a slime into <paramref name="childCount" /> half-sized slimes on death. Only fires
///     server-side, for slimes above the smallest size that actually died (rather than despawned).
/// </summary>
public sealed class SlimeSplitBehavior(int childCount = 4) : IEntityLifecycle
{
    public void OnMarkDead(EntityLiving self)
    {
        if (self is not EntitySlime slime) return;

        int size = slime.SlimeSize;
        if (slime.World.IsRemote || size <= 1 || slime.Health != 0) return;

        for (int i = 0; i < childCount; ++i)
        {
            float offsetX = (i % 2 - 0.5F) * size / 4.0F;
            float offsetY = (i * 0.5F - 0.5F) * size / 4.0F;
            EntitySlime child = new(slime.World) { SlimeSize = size / 2 };
            child.SetPositionAndAnglesKeepPrevAngles(slime.X + offsetX, slime.Y + 0.5D, slime.Z + offsetY, slime.Random.NextFloat() * 360.0F, 0.0F);
            slime.World.SpawnEntity(child);
        }
    }
}
