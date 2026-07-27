namespace BetaSharp.Entities;

/// <summary>
///     The configuration half of a mob: the values that are genuinely data rather than behavior.
///     A mob's class still owns its AI, state machines, and NBT — see
///     docs/mob-data-driven-migration.md for where that line sits and why.
///     <para>
///         Phase 2 keeps this a plain record with no protocol id, registry name, or spawn category.
///         Protocol ids already live on <see cref="EntityRegistry" />, and spawn category is derived
///         from the class hierarchy by <see cref="CreatureKind" /> rather than declared per mob;
///         both are Phase 3 concerns.
///     </para>
/// </summary>
public sealed record EntityDefinition
{
    /// <summary>Applied to any <see cref="EntityLiving" /> constructed without one (e.g. players).</summary>
    public static readonly EntityDefinition Default = new();

    public int Health { get; init; } = 10;
    public float MovementSpeed { get; init; } = 0.7F;

    /// <summary>Melee damage. Only consulted for <see cref="EntityCreature" /> and below.</summary>
    public int AttackStrength { get; init; } = 2;

    public float Width { get; init; } = 0.6F;
    public float Height { get; init; } = 1.8F;

    public string Texture { get; init; } = "/mob/char.png";

    public string? LivingSound { get; init; }
    public string? HurtSound { get; init; } = "random.hurt";
    public string? DeathSound { get; init; } = "random.hurt";
    public float SoundVolume { get; init; } = 1.0F;

    /// <summary>Ticks between idle-sound rolls; animals are quieter than monsters.</summary>
    public int TalkInterval { get; init; } = 80;

    public bool FireImmune { get; init; }
    public int MaxSpawnedInChunk { get; init; } = 4;
    public bool CanDespawn { get; init; } = true;
}
