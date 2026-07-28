namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Hurts a player who walks into the entity. Damage and reach both scale with the mob's declared
///     <c>size</c>, so a bigger one hits harder from further away. Read as a synced property rather
///     than off a class: this works on anything that declares a size, not specifically on a slime.
/// </summary>
public sealed class ContactDamageBehavior(double reachPerSize, int minimumSize, string sound) : IEntityInteractable
{
    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        if (self is not EntityLiving mob) return;

        int size = mob.Synced<byte>("size")?.Value ?? 1;
        if (size < minimumSize || !mob.CanSee(player) || !(mob.GetDistance(player) < reachPerSize * size)) return;

        if (player.Damage(mob, size))
        {
            mob.World.Broadcaster.PlaySoundAtEntity(mob, sound, 1.0F, (mob.Random.NextFloat() - mob.Random.NextFloat()) * 0.2F + 1.0F);
        }
    }
}
