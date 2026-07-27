using System.Text.Json.Serialization;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Entities;

/// <summary>
///     The configuration half of a mob: the values that are genuinely data rather than behavior.
///     A mob's class still owns its AI, state machines, and NBT — see
///     docs/mob-data-driven-migration.md for where that line sits and why.
///     <para>
///         Spawn category is still absent: it is derived from the class hierarchy by
///         <see cref="CreatureKind" /> rather than declared per mob, so a field here would compete
///         with that mechanism rather than describe it.
///     </para>
/// </summary>
public sealed record EntityDefinition : IDataAsset
{
    /// <summary>Applied to any <see cref="EntityLiving" /> constructed without one (e.g. players).</summary>
    public static readonly EntityDefinition Default = new();

    /// <summary>Set by the loader from the JSON filename.</summary>
    [JsonIgnore] public string Name { get; set; } = "";

    [JsonIgnore] public Namespace Namespace { get; set; } = Namespace.BetaSharp;

    /// <summary>
    ///     Wire protocol id. Spawn packets transmit it as a signed byte, so the loader rejects
    ///     anything outside 1..127; <c>-1</c> means "not declared" and fails validation.
    /// </summary>
    public int ProtocolId { get; init; } = -1;

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
