namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Splits the mob into <paramref name="childCount" /> half-sized copies of itself on death. The
///     children are created from the dying mob's own registered type, so this knows nothing about
///     what it is splitting — only that the type declares a <c>size</c> worth halving.
///     <para>
///         Server-side only, and only for a mob above the smallest size that actually died rather
///         than despawned.
///     </para>
/// </summary>
public sealed class SplitOnDeathBehavior(int childCount = 4) : IEntityLifecycle
{
    public void OnMarkDead(EntityLiving self)
    {
        if (self.World.IsRemote || self.Health != 0) return;
        if (self.Behaviors.Find<SizedBodyBehavior>() is not { } body) return;

        int size = body.Size(self);
        if (size <= 1) return;

        for (int i = 0; i < childCount; ++i)
        {
            float offsetX = (i % 2 - 0.5F) * size / 4.0F;
            float offsetZ = (i * 0.5F - 0.5F) * size / 4.0F;

            EntityLiving child = (EntityLiving)self.Type!.Create(self.World);
            body.SetSize(child, size / 2);
            child.SetPositionAndAnglesKeepPrevAngles(self.X + offsetX, self.Y + 0.5D, self.Z + offsetZ, self.Random.NextFloat() * 360.0F, 0.0F);
            self.World.SpawnEntity(child);
        }
    }
}
