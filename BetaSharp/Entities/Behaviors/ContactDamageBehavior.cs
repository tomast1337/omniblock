namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Hurts a player who walks into the entity. Damage and reach scale with a slime's size, so both
///     are expressed as a multiplier over <see cref="EntitySlime.SlimeSize" /> rather than fixed
///     numbers.
/// </summary>
public sealed class ContactDamageBehavior(double reachPerSize, int minimumSize, string sound) : IEntityInteractable
{
    public void OnPlayerCollision(Entity self, EntityPlayer player)
    {
        if (self is not EntitySlime slime) return;

        int size = slime.SlimeSize;
        if (size < minimumSize || !slime.CanSee(player) || !(slime.GetDistance(player) < reachPerSize * size)) return;

        if (player.Damage(slime, size))
        {
            slime.World.Broadcaster.PlaySoundAtEntity(slime, sound, 1.0F, (slime.Random.NextFloat() - slime.Random.NextFloat()) * 0.2F + 1.0F);
        }
    }
}
